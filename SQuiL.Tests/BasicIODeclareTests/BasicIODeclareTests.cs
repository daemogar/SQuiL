namespace SQuiL.Tests;

using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

public class BasicIODeclareTests
{
	private static string TestHeader(
		IEnumerable<string> attributes = default!,
		Func<string, string> callback = default!,
		[CallerMemberName] string name = default!)
	{
		attributes ??= [name];
		callback ??= p => $$"""
			[{{QueryAttributeName}}(QueryFiles.{{p}})]
			""";

		return $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};
		
			namespace TestCase;
		
			{{string.Join("", attributes.Select(callback))}}
			public partial class {{name}}DataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			""";
	}

	[Fact]
	public Task Input2Variable()
	{
		var name = nameof(Input2Variable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}			
			Declare	@Param_Object table(ObjectID int, IsMale bit, FirstName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task FullVariable()
	{
		var name = nameof(FullVariable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 1;

			Declare	@Param_Scaler int;
			
			Declare	@Param_Object table(ObjectID int, IsMale bit, FirstName varchar(100));
			
			Declare	@Params_Table table(TableID int, IsFemale bit, LastName varchar(100));
			
			Declare	@Return_Scaler int;
			
			Declare	@Return_Object table(ObjectID int, IsNeither bit, PreferredName varchar(100));
			
			Declare	@Returns_Table table(TableID int, IsBoth bit, NickName varchar(100));

			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task DateTypeVariable()
	{
		var name = nameof(DateTypeVariable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_AsOfDate date = Null;
			Declare @Returns_Rows table(RowID int, DateOfBirth date, BirthDate date);
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task TranscriptProcessing()
	{
		var name = nameof(TranscriptProcessing);
		return TestHelper.Verify([TestHeader([name])], [$$"""			
			--Name: {{name}}
			Declare @Param_IncludeCompletedTranscripts bit = 0;

			Declare @Debug bit = 1;

			Declare @Param_ReferenceID int Not Null;
			Declare @Param_IssueDate datetime;
			
			Declare @Param_Student table(
				ColleagueID varchar(50) Null,
				FirstName varchar(100),
				LastName varchar(100),
				MiddleName varchar(100) Null,
				DateOfBirth date Null);

			Declare @Param_Institution table(
				InstitutionID varchar(50) Null,
				SchoolName varchar(200),
				CEEB varchar(20) Not Null);

			Declare @Params_Courses table (
				Term varchar(10),
				Code varchar(20),
				Number varchar(20),
				Title varchar(200),
				Score varchar(5),
				Credits decimal);

			Use TranscriptProcessing;

			Begin Transaction;

			Begin Try

				If (Select 1 From TranscriptImports Where ReferenceID = @Param_ReferenceID) = 1 Begin

					Update    TranscriptImports
					Set            IssueDate = @Param_IssueDate,
									ReceivedAt = SysDateTimeOffset()
					Where        ReferenceID = @Param_ReferenceID;

				End
				Else Begin

					Insert Into TranscriptImports (ReferenceID, IssueDate, ReceivedAt)
					Values (@Param_ReferenceID, @Param_IssueDate, SysDateTimeOffset());

				End;

				Update    TranscriptImports
				Set            ColleagueID = ColleagueID,
								FirstName = FirstName,
								LastName = LastName ,
								MiddleName = MiddleName ,
								DateOfBirth = DateOfBirth
				From        TranscriptImports
								Cross Join @Param_Student
				Where        ReferenceID = @Param_ReferenceID;

				Update    InstitutionalRecords
				Set            InstitutionID = InstitutionID ,
								SchoolName = SchoolName ,
								CEEB = CEEB
				From        TranscriptImports
								Cross Join @Param_Institution
				Where        ReferenceID = @Param_ReferenceID;

				Delete    From TranscriptImportCourses
				Where        ReferenceID = @Param_ReferenceID;

				Insert Into TranscriptImportCourses (Term, Code, Number, Title, Score, Credits, ReferenceID)
				Select Term, Code, Number, Title, Score, Credits, @Param_ReferenceID
				From        @Params_Courses

				If @@TRANCOUNT > 0 Begin
					If @Debug = 1 Begin
						Rollback Transaction;
					End
					Else Begin
						Commit Transaction;
					End;
				End;
			End Try
			Begin Catch
				If @@TRANCOUNT > 0 Begin
					Rollback Transaction;
				End;

				Throw;
			End Catch
			"""]);
	}

	[Fact]
	public Task CustomTableVariable()
	{
		var name = nameof(CustomTableVariable);
		return TestHelper.Verify([$$"""
			{{TestHeader([name])}}

			[SQuiLTable(TableType.Table)]
			public partial record CustomFile {}
			"""], [$$"""
			--Name: {{name}}
			Declare	@Params_Table table(TableID int, IsFemale bit, LastName varchar(100));
			Declare	@Returns_Table table(TableID int, IsBoth bit, NickName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}
}
