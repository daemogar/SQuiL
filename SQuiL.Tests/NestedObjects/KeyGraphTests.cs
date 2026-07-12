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

    [Fact]
    public void ChildMatchingTwoPrimaryKeysIsAmbiguousError()
    {
        // "SharedID" is the PK of BOTH A and B; C carries SharedID → ambiguous parent.
        var g = Graph("""
            Declare @Returns_A table(SharedID int Primary Key, N int);
            Declare @Returns_B table(SharedID int Primary Key, M int);
            Declare @Returns_C table(CID int, SharedID int);
            Use Db; Select 1;
            """);
        Assert.Contains(g.Errors, f => f.Kind == "ambiguous" && f.Name == "C");
    }

    [Fact]
    public void CycleIsAnError()
    {
        // A.AID is PK, B carries AID (B child of A); B.BID is PK, A carries BID (A child of B) → cycle.
        var g = Graph("""
            Declare @Return_A table(AID int Primary Key, BID int);
            Declare @Return_B table(BID int Primary Key, AID int);
            Use Db; Select 1;
            """);
        Assert.Contains(g.Errors, f => f.Kind == "cycle");
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
