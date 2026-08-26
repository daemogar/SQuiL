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
    // consequences for these two fixtures, both accepted per the plan's pre-flight ruling:
    //   - Ambiguity detection ("a block links to >1 container") is deleted in this task outright —
    //     Task 2 reintroduces it under the new pair/order model.
    //   - Cycle detection is structurally impossible now: `childOf[Child] = Parent` always satisfies
    //     order(Parent) < order(Child), so no chain through `childOf` can ever return to its start.
    //     The cycle-detection code itself is retained unchanged (not deleted) per Ruling R2 — it
    //     simply never finds one under order-based edges. Task 3 (multi-container resolution, which
    //     inverts edges) is what makes cycles reachable again and may reopen this.
    [Fact]
    public void ChildMatchingTwoPrimaryKeysIsNotAmbiguousError()
    {
        // "SharedID" is the PK of BOTH A and B; C carries SharedID. Under declaration order this no
        // longer reports an "ambiguous" finding (Task 2 restores an ambiguity diagnostic here) — the
        // graph still builds without throwing.
        var g = Graph("""
            Declare @Returns_A table(SharedID int Primary Key, N int);
            Declare @Returns_B table(SharedID int Primary Key, M int);
            Declare @Returns_C table(CID int, SharedID int);
            Use Db; Select 1;
            """);
        Assert.DoesNotContain(g.Errors, f => f.Kind == "ambiguous");
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

        // TASK 3 (multi-container resolution, which inverts edges) may make this fixture cyclic
        // again. When that lands, uncomment the specific cycle assertions below:
        // Assert.Contains(g.Errors, f => f.Kind == "cycle");
        // var cycles = g.Errors.Where(f => f.Kind == "cycle").ToList();
        // Assert.Single(cycles);
        // Assert.Equal(new[] { "A", "B" }, new[] { cycles[0].Name, cycles[0].OtherName }.OrderBy(x => x).ToArray());
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
}
