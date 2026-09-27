using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;
using System.Linq;
using Xunit;

namespace SQuiL.Tests.NestedObjects;

public class KeyGraphTests
{
    private static SQuiLKeyGraph Graph(string sql)
    {
        var blocks = SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql));
        var outputs = blocks.Where(b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT
            && (b.IsTable || b.IsObject));
        return SQuiLKeyGraph.Build(outputs, sql);
    }

    private const string ThreeLevel = """
        Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);
        Declare @Return_Student table(StudentID int Primary Key, TranscriptID int, FirstName varchar(50));
        Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));
        Declare @Returns_Course table(CourseID int, InstitutionID int, Title varchar(50));
        Use Db;
        Select 1;
        """;

    [Fact]
    public void BuildsTreeRootsAndEdges()
    {
        var g = Graph(ThreeLevel);
        Assert.True(g.HasLinks);
        Assert.Empty(g.Errors);
        Assert.Equal(new[] { "Transcript" }, g.Roots.Select(r => r.Name).ToArray());

        // Transcript -> Student (via TranscriptID), Transcript -> Institution (TranscriptID),
        // Institution -> Course (InstitutionID).
        Assert.Equal(3, g.Edges.Count);
        Assert.Contains(g.Edges, e => e.Parent.Name == "Transcript" && e.Child.Name == "Student"     && e.KeyName == "TranscriptID");
        Assert.Contains(g.Edges, e => e.Parent.Name == "Transcript" && e.Child.Name == "Institution" && e.KeyName == "TranscriptID");
        Assert.Contains(g.Edges, e => e.Parent.Name == "Institution" && e.Child.Name == "Course"     && e.KeyName == "InstitutionID");
    }

    [Fact]
    public void NoPrimaryKeysAnywhereMeansNoLinks()
    {
        var g = Graph("""
            Declare @Returns_A table(AID int, N int);
            Declare @Returns_B table(BID int, M int);
            Use Db; Select 1;
            """);
        Assert.False(g.HasLinks);
        Assert.Empty(g.Edges);
        Assert.Equal(2, g.Roots.Count); // both flat siblings
    }

    // NOTE (Task 1 of the containment-direction feature, Ruling R2): declaration-order orientation
    // means every edge now points from the earlier-declared block to the later-declared one
    // (`SQuiLKeyEdge.Parent`/`Child` — see `order`/`pairs` in `SQuiLKeyGraph.Build`). Two direct
    // consequences, both accepted per the plan's pre-flight ruling:
    //   - The OLD ambiguity check ("a child's column matches >1 table's Primary Key") is deleted in
    //     Task 1 outright. Task 2 (this task, Ruling R0) reintroduces SP0033 under an entirely NEW
    //     condition — see `TwoBlocksSharingAKeyNameIsADuplicatePrimaryKeyError` below.
    //   - Cycle detection was structurally impossible directly out of `pairs`/edge construction:
    //     every RAW edge satisfies order(Parent) < order(Child), so no chain through `childOf` could
    //     ever return to its start. Task 3's R3 multi-container resolution (below `pairs` in
    //     `SQuiLKeyGraph.Build`) can INVERT an edge (new Parent = the higher-order block), which
    //     breaks that invariant and makes cycles reachable again — see
    //     `MultiContainerResolutionCanCascadeIntoACycle` below for a real, reachable one. A cycle
    //     needs 4+ blocks (proved exhaustively for 3 during Task 3 spike work); the classic 2-block
    //     reciprocal fixture below (`CycleIsNotAnError`) still can never cycle — see its comment.
    [Fact]
    public void TwoBlocksSharingAKeyNameIsADuplicatePrimaryKeyError()
    {
        // "SharedID" is declared `Primary Key` on BOTH A and B — R0 (Task 2) forbids this outright,
        // regardless of C carrying SharedID (that's a separate, unrelated concern: which table C
        // nests under). B is the second declaration, so it is the error's `Name`; A (first) is
        // `OtherName`.
        var g = Graph("""
            Declare @Returns_A table(SharedID int Primary Key, N int);
            Declare @Returns_B table(SharedID int Primary Key, M int);
            Declare @Returns_C table(CID int, SharedID int);
            Use Db; Select 1;
            """);
        var finding = Assert.Single(g.Errors, f => f.Kind == "duplicate-pk");
        Assert.Equal("B", finding.Name);
        Assert.Equal("A", finding.OtherName);
    }

    [Fact]
    public void CycleIsNotAnError()
    {
        // A.AID is PK, B carries AID; B.BID is PK, A carries BID — A and B are linked by TWO
        // reciprocal key columns. Under the OLD PK-oriented algorithm this was a two-node cycle
        // (A child-of B and B child-of A). Under declaration-order orientation both matches point
        // A -> B (A declared first), so no cycle can form — a structural consequence of R1, not a
        // bug in this fixture.
        var g = Graph("""
            Declare @Return_A table(AID int Primary Key, BID int);
            Declare @Return_B table(BID int Primary Key, AID int);
            Use Db; Select 1;
            """);
        Assert.DoesNotContain(g.Errors, f => f.Kind == "cycle");
        Assert.All(g.Edges, e => Assert.Equal("A", e.Parent.Name));

        // C1 regression: two reciprocal key columns between the SAME pair must still dedupe to
        // exactly one edge (a naive per-key dedupe produced two edges with the same Parent/Child,
        // which the generator turned into a duplicate emitted member — CS0102). The first matching
        // key column found in declaration order (A's BID column, matching B's own Primary Key) wins.
        var edge = Assert.Single(g.Edges);
        Assert.Equal("A", edge.Parent.Name);
        Assert.Equal("B", edge.Child.Name);
        Assert.Equal("BID", edge.KeyName);
        Assert.True(edge.IsEmbed);

        // TASK 3 FOLLOW-UP (resolved, not just deferred): Task 3's R3 resolution runs over GROUPS of
        // edges that share a Child, dropping/inverting all-but-the-earliest container. This 2-block
        // fixture dedupes to exactly ONE edge (asserted above, via `pairSeen` in `SQuiLKeyGraph.Build`
        // — a pair of blocks can only ever produce one edge between them, however many reciprocal key
        // columns they share), so no `Child` here is EVER the Child of 2+ edges — R3's resolution loop
        // has nothing to group and never runs. A cycle requires R3 to actually invert something, which
        // in turn requires a genuine multi-container conflict; that's structurally unreachable with
        // only 2 blocks (verified exhaustively over every 3-block pair/owner/key-sharing configuration
        // too — see `MultiContainerResolutionCanCascadeIntoACycle` below for the minimal 4-block
        // fixture that DOES cycle). So this fixture staying acyclic is permanent, not provisional.
    }

    [Fact]
    public void EmbedDirection_WhenFkCarrierDeclaredFirst()
    {
        // Structure (the FK carrier) is declared before Contact (the PK owner) — the lookup is
        // embedded into its container. R1: the earlier-declared block (Structure) is still the
        // container/Parent, but IsEmbed is true because the nested block (Contact) owns the key.
        var sql = """
            Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));
            Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));
            Use [Db];
            Select * From @Returns_Structure;
            Select * From @Returns_Contact;
            """;

        var g = Graph(sql);

        var edge = Assert.Single(g.Edges);
        Assert.Equal("Structure", edge.Parent.Name);
        Assert.Equal("Contact", edge.Child.Name);
        Assert.True(edge.IsEmbed);
        Assert.Equal("Structure", Assert.Single(g.Roots).Name);
    }

    [Fact]
    public void ChildDirection_WhenPkOwnerDeclaredFirst_IsUnchanged()
    {
        // Transcript (the PK owner) is declared before Institution (the FK carrier) — today's classic
        // one-to-many shape. IsEmbed is false because the nested block does NOT own the key.
        var sql = """
            Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);
            Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));
            Use [Db];
            Select * From @Return_Transcript;
            Select * From @Returns_Institution;
            """;

        var g = Graph(sql);

        var edge = Assert.Single(g.Edges);
        Assert.Equal("Transcript", edge.Parent.Name);
        Assert.Equal("Institution", edge.Child.Name);
        Assert.False(edge.IsEmbed);
    }

    // Plan Task 7: an embedded lookup's PK is linked (from the container), so it is not orphaned.
    [Fact]
    public void OrphanHintDoesNotFireOnAnEmbeddedPrimaryKey()
    {
        var g = Graph("""
            Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));
            Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));
            Use [Db];
            Select * From @Returns_Structure;
            Select * From @Returns_Contact;
            """);
        Assert.True(g.HasLinks);
        Assert.Empty(g.Hints);
    }

    [Fact]
    public void OrphanPrimaryKeyIsAHintOnlyWhenNestingIsInPlay()
    {
        // X has a PK nobody links to, but a real link exists elsewhere (A->B), so nesting is "in play".
        var g = Graph("""
            Declare @Returns_A table(AID int Primary Key, N int);
            Declare @Returns_B table(BID int, AID int);
            Declare @Returns_X table(XID int Primary Key, M int);
            Use Db; Select 1;
            """);
        Assert.Contains(g.Hints, f => f.Kind == "orphan" && f.Name == "X");
        Assert.DoesNotContain(g.Hints, f => f.Name == "A"); // A's PK is linked by B
    }

    // ── Task 3: R3 multi-container resolution ────────────────────────────────

    [Fact]
    public void JunctionKeepsEarliestContainerAndInvertsTheRest()
    {
        var sql = """
            Declare @Returns_Student table(StudentID int Primary Key, Name varchar(50));
            Declare @Returns_Course table(CourseID int Primary Key, Title varchar(50));
            Declare @Returns_Enrollment table(StudentID int, CourseID int, Grade varchar(2));
            Use [Db];
            Select * From @Returns_Student;
            Select * From @Returns_Course;
            Select * From @Returns_Enrollment;
            """;

        var g = Graph(sql);

        Assert.Equal(2, g.Edges.Count);
        var kept = Assert.Single(g.Edges, e => e.Child.Name == "Enrollment");
        Assert.Equal("Student", kept.Parent.Name);
        Assert.False(kept.IsEmbed);

        var inverted = Assert.Single(g.Edges, e => e.Child.Name == "Course");
        Assert.Equal("Enrollment", inverted.Parent.Name);
        Assert.True(inverted.IsEmbed);

        Assert.Equal("Student", Assert.Single(g.Roots).Name);

        // Review round 1, I3 regression: Course's Primary Key IS linked (Enrollment embeds it) —
        // it must NOT be flagged orphan just because Course is never a `Parent` (the old, wrong
        // orphan predicate). This is a regression Task 3's own inversion introduced: pre-R3 the
        // edge was Course->Enrollment (Course as Parent), so the old predicate never misfired here.
        Assert.Empty(g.Hints);
    }

    [Fact]
    public void SharedLookupAllowsOnePkOwnerInManyContainers()
    {
        var sql = """
            Declare @Returns_Structure table(Title varchar(50), ContactID varchar(10));
            Declare @Returns_Widget table(Label varchar(50), ContactID varchar(10));
            Declare @Returns_Contact table(ContactID varchar(10) Primary Key, Name varchar(50));
            Use [Db];
            Select * From @Returns_Structure;
            Select * From @Returns_Widget;
            Select * From @Returns_Contact;
            """;

        var g = Graph(sql);

        Assert.Equal(2, g.Edges.Count);
        Assert.All(g.Edges, e => Assert.True(e.IsEmbed));
        Assert.All(g.Edges, e => Assert.Equal("Contact", e.Child.Name));
        Assert.Equal(["Structure", "Widget"], g.Roots.Select(r => r.Name).ToArray());

        // Review round 1, I3 regression: Contact's Primary Key IS linked (both Structure and
        // Widget embed it) — must not be flagged orphan just because Contact is never a `Parent`.
        Assert.Empty(g.Hints);
    }

    [Fact]
    public void MultiContainerResolutionIteratesToAFixedPoint()
    {
        // A single R3 pass is NOT enough here. A owns AID and carries a column CID (an FK to C);
        // B owns BID and carries a column AID (an FK to A); C owns CID and carries a column BID
        // (an FK to B) — a rotation of three "many owns one" links, plus A additionally referencing
        // C directly (A embeds C, since C is declared after A and owns the shared key).
        //
        // Pass 1 resolves C's conflict (A embeds C vs. B contains C): A wins (declared first), so
        // B's B->C edge is dropped and inverted (C owns CID) into a NEW edge C->B. That INVERTED
        // edge is what creates a SECOND, previously-nonexistent conflict at B (A->B, from A's own
        // AID column, vs. the brand-new C->B) — pass 2 resolves it: A wins again (declared first),
        // and this time C->B is dropped WITHOUT re-inverting, because the edge being dropped is
        // already an embed (C does not own BID — B does — so the invert guard's `pkNameOf[dropped
        // Parent] == dropped key` check fails and the link is simply discarded).
        //
        // A naive single-pass implementation would stop after pass 1 and report 3 edges (A->B,
        // A->C, C->B) with B double-parented; the fixed-point loop converges on exactly 2.
        var sql = """
            Declare @Returns_A table(AID int Primary Key, CID int);
            Declare @Returns_B table(BID int Primary Key, AID int);
            Declare @Returns_C table(CID int Primary Key, BID int);
            Use [Db];
            Select * From @Returns_A;
            Select * From @Returns_B;
            Select * From @Returns_C;
            """;

        var g = Graph(sql);

        Assert.Empty(g.Errors);
        Assert.Equal(2, g.Edges.Count);

        var toB = Assert.Single(g.Edges, e => e.Child.Name == "B");
        Assert.Equal("A", toB.Parent.Name);
        Assert.Equal("AID", toB.KeyName);
        Assert.False(toB.IsEmbed);

        var toC = Assert.Single(g.Edges, e => e.Child.Name == "C");
        Assert.Equal("A", toC.Parent.Name);
        Assert.Equal("CID", toC.KeyName);
        Assert.True(toC.IsEmbed);

        Assert.Equal("A", Assert.Single(g.Roots).Name);
    }

    [Fact]
    public void MultiContainerResolutionCanCascadeIntoACycle()
    {
        // The minimal reachable cycle (verified by exhaustive search over every 3-block
        // configuration — none cycle; this is the smallest of the 4-block configurations that do).
        //
        // Category owns CategoryID; Product owns ProductID and carries CategoryID (an FK to
        // Category); Junction carries BOTH CategoryID (FK to Category) and ProductID (FK to
        // Product) — the classic many-to-many junction shape. Summary, declared FIRST, carries
        // ProductID too (an FK to Product) — since Product is declared after Summary and owns the
        // shared key, this is an embed (Summary embeds Product).
        //
        // Pass 1: Product has two competing containers — Summary (embed, declared first) and
        // Category (plain child, via Product's own CategoryID column). Summary wins (lower
        // declaration order); Category's Category->Product edge is dropped and INVERTED (Product
        // owns ProductID... no — the dropped edge's key is CategoryID, owned by Category) into
        // Product->Category.
        // Pass 2: Junction has two competing containers — Category (plain child, via CategoryID)
        // and Product (plain child, via ProductID). Category wins (lower declaration order);
        // Product's Product->Junction edge is dropped and INVERTED (Product owns ProductID) into
        // Junction->Product.
        // Pass 3: no group qualifies as a conflict — Product is now Child of both Summary (embed)
        // and Junction (embed), an ALL-embed shared-lookup group, which R3 allows to stand.
        //
        // The surviving edges are Summary->Product, Category->Junction, and Junction->Product —
        // PLUS, from pass 1, Product->Category. Category->Junction->Product->Category is a genuine
        // 3-block cycle among Category/Product/Junction: each contains the next.
        var sql = """
            Declare @Returns_Summary table(ProductID varchar(10));
            Declare @Returns_Category table(CategoryID int Primary Key, Name varchar(50));
            Declare @Returns_Product table(ProductID varchar(10) Primary Key, CategoryID int, Title varchar(50));
            Declare @Returns_Junction table(CategoryID int, ProductID varchar(10), Note varchar(50));
            Use [Db];
            Select * From @Returns_Summary;
            Select * From @Returns_Category;
            Select * From @Returns_Product;
            Select * From @Returns_Junction;
            """;

        var g = Graph(sql);

        var cycles = g.Errors.Where(f => f.Kind == "cycle").ToList();
        var cycle = Assert.Single(cycles);
        // The cycle's two named participants are both drawn from {Category, Product, Junction} —
        // the three blocks actually rotating containment — never Summary, which only ever embeds
        // Product and is not itself part of the loop.
        Assert.Subset(new HashSet<string> { "Category", "Product", "Junction" },
            new HashSet<string> { cycle.Name, cycle.OtherName });
        Assert.DoesNotContain("Summary", new[] { cycle.Name, cycle.OtherName });
    }

    // ── Review round 1 fixes ────────────────────────────────────────────────

    [Fact]
    public void CycleThroughACollapsedChildOfEntryIsStillDetected()
    {
        // C1 regression: B ends up with TWO valid embed parents post-R3 (C and D) — a legitimate
        // shared-lookup shape on its own. But C ALSO has an edge back from E (E->C, embed), and B
        // also contains E (B->E) — closing a genuine cycle C->B->E->C that a `childOf`-based walk
        // (one entry per Child, overwritten by whichever edge is enumerated LAST) can miss
        // entirely: if D->B happens to be the surviving `childOf[B]` entry, walking from C never
        // sees the C->B edge that actually closes the loop, and SP0034 stays silent. A DFS over
        // the FULL edge set must find this regardless of which single parent `childOf` would have
        // kept. (A missed cycle here is not just a wrong diagnostic — FileGenerator.cs only bails
        // out of code generation when `SQuiLKeyGraph.Errors` is non-empty; a graph that reaches
        // SQuiLDataContext.cs with an undetected cycle sends `DeepestFirstEdges`'s `Visit` into
        // unbounded recursion — a compiler-crashing stack overflow, not a bad diagnostic. See
        // `NestedDiagnosticsTests.FiveBlockCycleThroughACollapsedChildOfEntryReportsSP0034AtBuildTime`
        // for the full-pipeline version of this same fixture — the one that would actually crash
        // the test host without this fix.)
        var sql = """
            Declare @Returns_A table(AID int Primary Key, CID int);
            Declare @Returns_B table(BID int Primary Key, N int);
            Declare @Returns_C table(CID int Primary Key, BID int);
            Declare @Returns_D table(DN int, AID int, BID int);
            Declare @Returns_E table(EN int, BID int, CID int);
            Use [Db];
            Select * From @Returns_A;
            Select * From @Returns_B;
            Select * From @Returns_C;
            Select * From @Returns_D;
            Select * From @Returns_E;
            """;

        var g = Graph(sql);

        var cycles = g.Errors.Where(f => f.Kind == "cycle").ToList();
        Assert.Single(cycles);
    }

    [Fact]
    public void MultiContainerResolutionReachesTheTrueFixedPointBeyondTheOldGuardBound()
    {
        // C2 regression: the OLD guard bound (`list.Count + 1` = 6 for these 5 blocks) cuts this
        // fixture off after only 6 resolving passes, leaving C multi-parented — A->C (non-embed)
        // survives alongside a NOT-yet-dropped E->C (embed) — which is not a fixed point at all
        // under R3's own predicate (a mixed embed/non-embed group is exactly what must keep
        // resolving). That wrong, premature stop compiles silently: C would get a spurious
        // `List<Models.C>? C` member on E, with no error and no hint (pair dedupe prevents
        // CS0102). The TRUE fixed point needs 7 resolving passes (an 8th check confirms no
        // conflict remains) and converges on a flat tree: A (order 0, the global tie-winner) ends
        // up the sole container of B, C, D, and E directly; every intermediate B/C/D/E cross-link
        // this fixture declares gets discarded along the way (none of B/D/E owns a key the losing
        // side could invert into), and C's only surviving relationship is directly to A.
        var sql = """
            Declare @Returns_A table(AID int Primary Key, N int);
            Declare @Returns_B table(BID int Primary Key, AID int);
            Declare @Returns_C table(CID int Primary Key, AID int, BID int);
            Declare @Returns_D table(DN int, AID int, BID int);
            Declare @Returns_E table(EN int, AID int, BID int, CID int);
            Use [Db];
            Select * From @Returns_A;
            Select * From @Returns_B;
            Select * From @Returns_C;
            Select * From @Returns_D;
            Select * From @Returns_E;
            """;

        var g = Graph(sql);

        Assert.Empty(g.Errors);
        Assert.Equal(4, g.Edges.Count);
        Assert.All(g.Edges, e => Assert.Equal("A", e.Parent.Name));
        Assert.All(g.Edges, e => Assert.False(e.IsEmbed));
        Assert.Equal(new[] { "B", "C", "D", "E" }, g.Edges.Select(e => e.Child.Name).OrderBy(x => x).ToArray());
        Assert.Equal("A", Assert.Single(g.Roots).Name);
    }
}
