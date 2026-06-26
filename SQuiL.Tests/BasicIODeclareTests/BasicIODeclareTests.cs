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
		// @Param_Object/@Return_Object and @Params_Table/@Returns_Table share
		// generated record types, so their column lists must be identical
		// (mismatched shapes are an SP0017 error — see TableNameMergeTests).
		var name = nameof(FullVariable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Debug bit = 1;

			Declare	@Param_Scaler int;

			Declare	@Param_Object table(ObjectID int, IsMale bit, FirstName varchar(100));

			Declare	@Params_Table table(TableID int, IsFemale bit, LastName varchar(100));

			Declare	@Return_Scaler int;

			Declare	@Return_Object table(ObjectID int, IsMale bit, FirstName varchar(100));

			Declare	@Returns_Table table(TableID int, IsFemale bit, LastName varchar(100));

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
	public Task DateTimeOffsetVariable()
	{
		var name = nameof(DateTimeOffsetVariable);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @AsOfDate datetimeoffset = Null;
			Declare @Param_CreatedAt datetimeoffset;
			Declare @Return_ModifiedAt datetimeoffset;
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task ScalarVariableDefaults()
	{
		// Non-null defaults for scalar types must emit a compiling initializer.
		// uniqueidentifier in particular must parse, not assign a bare string.
		var name = nameof(ScalarVariableDefaults);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_Id uniqueidentifier = '12345678-1234-1234-1234-123456789012';
			Declare @Param_AmountWhole decimal(18,2) = 5;
			Declare @Param_Count int = 7;
			Declare @Param_Ratio float = 2;
			Declare @Param_Flag bit = 1;
			Declare @Param_Label varchar(50) = 'hello';
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task DateFamilyVariableDefaults()
	{
		// Non-null defaults for every date-family type must emit a compiling
		// initializer (a parse expression, not a bare quoted string).
		var name = nameof(DateFamilyVariableDefaults);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_OnDate date = '2024-01-01';
			Declare @Param_AtTime time = '13:45:30';
			Declare @Param_StampedAt datetime = '2024-01-01 13:45:30';
			Declare @Param_CreatedAt datetimeoffset = '2024-01-01 12:00:00 +05:00';
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task MultiLineCommentInHeader()
	{
		// A multi-line comment in the header is tokenized as COMMENT_MULTILINE;
		// its dumped value must be line-ending-independent (no embedded CR).
		var name = nameof(MultiLineCommentInHeader);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			/* multi
			   line
			   comment */
			Declare @Param_Count int;
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task FractionalNumericDefaults()
	{
		// Fractional defaults must tokenize (not SP1001) and compile:
		// decimal needs an 'm' suffix; double accepts the bare literal.
		var name = nameof(FractionalNumericDefaults);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_Amount decimal(18,2) = 1.5;
			Declare @Param_Ratio float = 2.5;
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task ColumnDefaultsTrailing()
	{
		// Column defaults produce a hybrid record: non-defaulted columns are positional
		// constructor parameters; defaulted columns become { get; init; } = <value> properties,
		// reusing the per-type CSharpValue logic (decimal 'm', string quotes).
		var name = nameof(ColumnDefaultsTrailing);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Params_Rows table(RowID int, Amount decimal(18,2) default 1.5, Note varchar(50) default 'hello');
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task ColumnDefaultBeforeRequired()
	{
		// A default may now sit before a required column: the table becomes a hybrid
		// record (positional ctor for non-defaulted columns + init props for defaulted).
		// (SP0010 now used by the editor nullability hint.)
		var name = nameof(ColumnDefaultBeforeRequired);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Params_Rows table(RowID int, Amount decimal(18,2) default 1.5, Qty int, Note varchar(50) default 'hello');
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task AllColumnsDefaulted()
	{
		// Every column defaulted: the positional ctor is empty (record Name()) and the
		// return-table reader builds rows via a bare object initializer (new() { ... }).
		// Locks the empty-positional-list branch in both SQuiLTable and SQuiLDataContext.
		var name = nameof(AllColumnsDefaulted);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Rows table(Amount decimal(18,2) default 1.5, Note varchar(50) default 'hello');
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task TypeKeywordAsColumnName()
	{
		var name = nameof(TypeKeywordAsColumnName);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Records table(RecordID int, Date date, Time time, DateTime datetime);
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
			Declare @Debug bit = 1;

			Declare @Param_IncludeCompletedTranscripts bit = 0;

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
	public Task NotPrefixedColumnNames()
	{
		var name = nameof(NotPrefixedColumnNames);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Records table(
				RecordID int,
				Notes varchar(100),
				NotEnrolled bit Not Null,
				Notification varchar(100) Null,
				NullableFlag bit Null);
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task ReturnTableColumnDefault()
	{
		// A return table with a default must read into the hybrid record via an
		// object initializer for the defaulted column(s).
		var name = nameof(ReturnTableColumnDefault);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Rows table(RowID int, Amount decimal(18,2) default 1.5, Qty int);
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task ScalarNullabilityMatrix()
	{
		var name = nameof(ScalarNullabilityMatrix);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Param_Required int;
			Declare @Param_Nullable int null;
			Declare @Param_NotNull int not null;
			Declare @Param_Defaulted int = 5;
			Declare @Param_NullAndDefault int null = 5;
			Declare @Return_Count int;
			Use MyDb;
			Select @Return_Count = 1;
			"""]);
	}

	[Fact]
	public Task CustomTableVariable()
	{
		// @Params_Table/@Returns_Table share the CustomFile record, so their
		// column lists must be identical (mismatches are an SP0017 error).
		var name = nameof(CustomTableVariable);
		return TestHelper.Verify(sources: [$$"""
			{{TestHeader([name])}}

			[SQuiLTable(TableType.Table)]
			public partial record CustomFile {}
			"""], files: [$$"""
			--Name: {{name}}
			Declare	@Params_Table table(TableID int, IsFemale bit, LastName varchar(100));
			Declare	@Returns_Table table(TableID int, IsFemale bit, LastName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task CustomTableVariableWithPrimaryConstructor()
	{
		var name = nameof(CustomTableVariableWithPrimaryConstructor);
		return TestHelper.Verify(sources: [$$"""
			{{TestHeader([name])}}

			[SQuiLTable(TableType.Table)]
			public partial record CustomFile(int TableID);
			"""], files: [$$"""
			--Name: {{name}}
			Declare @Returns_Table table(TableID int, IsBoth bit, NickName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task CustomTableVariableWithColumnDefault()
	{
		// Merge path (user partial with empty primary ctor) + a column default:
		// the defaulted column becomes an init property; non-defaulted columns
		// remain positional ctor params.
		var name = nameof(CustomTableVariableWithColumnDefault);
		return TestHelper.Verify(sources: [$$"""
			{{TestHeader([name])}}

			[SQuiLTable(TableType.Table)]
			public partial record CustomFile() {}
			"""], files: [$$"""
			--Name: {{name}}
			Declare	@Params_Table table(TableID int, IsActive bit default 1, LastName varchar(100));
			Declare	@Returns_Table table(TableID int, IsActive bit default 1, LastName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task SameNameTableAndObjectShareOneRecord()
	{
		var name = nameof(SameNameTableAndObjectShareOneRecord);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Declare @Return_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}

	[Fact]
	public Task RowRecordsEmitIntoModelsSubNamespace()
	{
		var name = nameof(RowRecordsEmitIntoModelsSubNamespace);
		return TestHelper.Verify([TestHeader([name])], [$$"""
			--Name: {{name}}
			Declare @Returns_Person table(PersonID int, FullName varchar(100));
			Use [Database];
			Select 1;
			"""]);
	}
}
