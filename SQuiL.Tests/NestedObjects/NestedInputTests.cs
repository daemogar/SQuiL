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
