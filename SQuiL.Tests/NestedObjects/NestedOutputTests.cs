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

    // Two tables that each declare a Primary Key, but neither carries the
    // other's key-column name. HasLinks must be false, so both stay flat
    // top-level Response siblings and the generated DataContext must use
    // the flat reader (no __<Name> locals, no stitch).
    [Fact]
    public Task PrimaryKeysNoLinks()
    {
        var name = nameof(PrimaryKeysNoLinks);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_Alpha table(AlphaID int Primary Key, N int);
            Declare @Returns_Beta table(BetaID int Primary Key, M int);
            Use [Db];
            Insert Into @Returns_Alpha Select AlphaID, N From A;
            Insert Into @Returns_Beta Select BetaID, M From B;
            Select * From @Returns_Alpha;
            Select * From @Returns_Beta;
            """]);
    }

    // The Transcript/Institution/Course nest, plus an unrelated scalar and an
    // unrelated table with no key relationship to anything. Transcript nests
    // as before; Total and Log remain flat top-level Response properties.
    [Fact]
    public Task NestedPlusUnrelatedSiblings()
    {
        var name = nameof(NestedPlusUnrelatedSiblings);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Return_Transcript table(TranscriptID int Primary Key, IssueDate date);
            Declare @Returns_Institution table(InstitutionID int Primary Key, TranscriptID int, SchoolName varchar(50));
            Declare @Returns_Course table(CourseID int, InstitutionID int, Title varchar(50));
            Declare @Return_Total int;
            Declare @Returns_Log table(LogID int, Message varchar(50));
            Use [Db];
            Insert Into @Return_Transcript Select TranscriptID, IssueDate From T;
            Insert Into @Returns_Institution Select InstitutionID, TranscriptID, SchoolName From I;
            Insert Into @Returns_Course Select CourseID, InstitutionID, Title From C;
            Set @Return_Total = (Select Count(*) From T);
            Insert Into @Returns_Log Select LogID, Message From L;
            Select * From @Return_Transcript;
            Select * From @Returns_Institution;
            Select * From @Returns_Course;
            Select @Return_Total As Total;
            Select * From @Returns_Log;
            """]);
    }

    // Embedded lookup: Structure carries ContactID, Contact owns it as its Primary Key.
    // Contact embeds into Structure as a single object; Structure drops its ContactID member.
    [Fact]
    public Task EmbeddedLookup()
    {
        var name = nameof(EmbeddedLookup);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
            Declare @Returns_Contact table(ContactID varchar(10) not null Primary Key, Name varchar(50) not null);
            Use [Db];
            Select * From @Returns_Structure;
            Select * From @Returns_Contact;
            """]);
    }

    // Shared lookup: Structure and Widget both embed Contact; each container gets its own stitch.
    [Fact]
    public Task SharedLookup()
    {
        var name = nameof(SharedLookup);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_Structure table(Title varchar(50) not null, ContactID varchar(10) not null);
            Declare @Returns_Widget table(Label varchar(50) not null, ContactID varchar(10) not null);
            Declare @Returns_Contact table(ContactID varchar(10) not null Primary Key, Name varchar(50) not null);
            Use [Db];
            Select * From @Returns_Structure;
            Select * From @Returns_Widget;
            Select * From @Returns_Contact;
            """]);
    }

    // Many-to-many junction: Enrollment nests under Student (earliest container) and embeds Course.
    [Fact]
    public Task ManyToManyJunction()
    {
        var name = nameof(ManyToManyJunction);
        return TestHelper.Verify([TestHelper.TestHeaderPublic([name])], [$$"""
            --Name: {{name}}
            Declare @Returns_Student table(StudentID int not null Primary Key, Name varchar(50) not null);
            Declare @Returns_Course table(CourseID int not null Primary Key, Title varchar(50) not null);
            Declare @Returns_Enrollment table(StudentID int not null, CourseID int not null, Grade varchar(2) not null);
            Use [Db];
            Select * From @Returns_Student;
            Select * From @Returns_Course;
            Select * From @Returns_Enrollment;
            """]);
    }
}
