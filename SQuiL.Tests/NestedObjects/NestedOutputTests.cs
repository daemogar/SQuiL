namespace SQuiL.Tests.NestedObjects;

using System.Threading.Tasks;
using Xunit;

public class NestedOutputTests
{
    // 3-level list nesting under an object root: Transcript (object root)
    //   -> Institution (list) -> Course (list). Exercises object-root SingleOrDefault,
    // list children, and a nested grandchild.
    [Fact]
    public Task ThreeLevelListNesting()
    {
        var name = nameof(ThreeLevelListNesting);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);
            Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));
            Declare @Returns_Course table(CourseID int, InstitutionID int, Title varchar(50));
            Use [Db];
            Insert Into @Return_Transcript Select TranscriptID, IssueDate From T;
            Insert Into @Returns_Institution Select InstitutionID, TranscriptID, SchoolName From I;
            Insert Into @Returns_Course Select CourseID, InstitutionID, Title From C;
            Select * From @Return_Transcript;
            Select * From @Returns_Institution;
            Select * From @Returns_Course;
            """]);
    }

    // Embedded object child: Transcript (object root) -> Student (single object child).
    // Exercises the object-child SingleOrDefault stitch (not a list).
    [Fact]
    public Task EmbeddedObjectChild()
    {
        var name = nameof(EmbeddedObjectChild);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);
            Declare @Return_Student table(StudentID int Primary Key, TranscriptID int, FirstName varchar(50));
            Use [Db];
            Insert Into @Return_Transcript Select TranscriptID, IssueDate From T;
            Insert Into @Return_Student Select StudentID, TranscriptID, FirstName From S;
            Select * From @Return_Transcript;
            Select * From @Return_Student;
            """]);
    }
}
