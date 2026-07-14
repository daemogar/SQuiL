namespace SQuiL.Tests.NestedObjects;

using System.Threading.Tasks;
using Xunit;

public class NestedInputTests
{
    // 3-level INPUT nesting under an object root: Transcript (object root)
    //   -> Institution (list) -> Course (list). The request envelope must expose
    // ONLY Transcript; Institution/Course collapse into settable list members
    // (`= []`) on their parent records. The DataContext must flatten the caller's
    // nested request into per-table `__<Name>` lists, synthesizing 1-based
    // sequential int keys (PK) and propagating each parent's synthesized PK into
    // its children's FK column, then feed each flat list to AddJsonParameter.
    [Fact]
    public Task ThreeLevelInputNesting()
    {
        var name = nameof(ThreeLevelInputNesting);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Param_Transcript table(TranscriptID int Primary Key, IssueDate date);
            Declare @Params_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));
            Declare @Params_Course table(CourseID int, InstitutionID int, Title varchar(50));
            Use [Db];
            Insert Into dbo.Transcripts Select TranscriptID, IssueDate From @Param_Transcript;
            Insert Into dbo.Institutions Select InstitutionID, TranscriptID, SchoolName From @Params_Institution;
            Insert Into dbo.Courses Select CourseID, InstitutionID, Title From @Params_Course;
            """]);
    }

    // GUID-keyed INPUT nesting: Order (object root, uniqueidentifier PK) ->
    // Line (list, uniqueidentifier PK + OrderID FK). Synthesized keys must be
    // System.Guid.NewGuid() (not sequential), and Line.OrderID must receive the
    // parent order's synthesized guid.
    [Fact]
    public Task GuidKeyInputNesting()
    {
        var name = nameof(GuidKeyInputNesting);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Param_Order table(OrderID uniqueidentifier Primary Key, Placed date);
            Declare @Params_Line table(LineID uniqueidentifier Primary Key, OrderID uniqueidentifier, Sku varchar(50));
            Use [Db];
            Insert Into dbo.Orders Select OrderID, Placed From @Param_Order;
            Insert Into dbo.Lines Select LineID, OrderID, Sku From @Params_Line;
            """]);
    }

    // Leaf-owns-its-own-PK regression: Order (object root, int PK) -> Line (list,
    // int PK on LineID + OrderID FK). Line is a LEAF (no children of its own) but
    // still declares `Primary Key` on LineID. Its own key must be synthesized
    // (1-based sequential, since LineID is int) rather than copied from the
    // caller's `line.LineID` — a leaf that owns a PK is still part of the
    // relationship tree and must never be left to the caller to hand-manage.
    [Fact]
    public Task LeafOwnPrimaryKeyInputNesting()
    {
        var name = nameof(LeafOwnPrimaryKeyInputNesting);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Param_Order table(OrderID int Primary Key, Placed date);
            Declare @Params_Line table(LineID int Primary Key, OrderID int, Sku varchar(50));
            Use [Db];
            Insert Into dbo.Orders Select OrderID, Placed From @Param_Order;
            Insert Into dbo.Lines Select LineID, OrderID, Sku From @Params_Line;
            """]);
    }

    // Guardrail regression: an ISOLATED input table (its own PK, but no edge
    // touches it — nobody references CategoryID as a FK, and it references
    // nobody else's PK) sits alongside a genuinely-linked Order -> Line tree in
    // the SAME file (so EffectiveInputGraph.HasLinks is true). Category must
    // keep the existing 1:1 caller-supplied copy path — its own CategoryID is
    // NOT synthesized, even though it declares a Primary Key.
    [Fact]
    public Task IsolatedTableWithOwnPrimaryKeyUnaffectedByLinkedSiblings()
    {
        var name = nameof(IsolatedTableWithOwnPrimaryKeyUnaffectedByLinkedSiblings);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Param_Order table(OrderID int Primary Key, Placed date);
            Declare @Params_Line table(LineID int Primary Key, OrderID int, Sku varchar(50));
            Declare @Params_Category table(CategoryID int Primary Key, Label varchar(50));
            Use [Db];
            Insert Into dbo.Orders Select OrderID, Placed From @Param_Order;
            Insert Into dbo.Lines Select LineID, OrderID, Sku From @Params_Line;
            Insert Into dbo.Categories Select CategoryID, Label From @Params_Category;
            """]);
    }

    // SP0036: a nested INPUT link column whose declared type is neither
    // integer-family nor uniqueidentifier (here varchar TranscriptCode) cannot
    // have a key synthesized. The generator must report SP0036 and skip emitting
    // this file's models / data-context. compileCheck:false — the error path
    // deliberately produces no generated output to compile.
    [Fact]
    public Task UnsupportedLinkTypeInput()
    {
        var name = nameof(UnsupportedLinkTypeInput);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Param_Transcript table(TranscriptCode varchar(10) Primary Key, IssueDate date);
            Declare @Params_Institution table(InstitutionID int Primary Key, TranscriptCode varchar(10), SchoolName varchar(50));
            Use [Db];
            Insert Into dbo.Transcripts Select TranscriptCode, IssueDate From @Param_Transcript;
            Insert Into dbo.Institutions Select InstitutionID, TranscriptCode, SchoolName From @Params_Institution;
            """], compileCheck: false);
    }
}
