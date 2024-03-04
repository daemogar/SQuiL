using System.Data;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SquilParser.Tests;

public class DataContextGenerationTests //: DataContextUsedTests
{
	private static readonly string[] queries = [
		@"Queries\Example",
		@"Queries\GetStudentCoursesForEvaluation"
	];

	[Fact] public Task AllQueries() => Verify(nameof(AllQueries), queries);
	[Fact] public Task Example() => Verify(nameof(Example), [queries[0]]);
	[Fact] public Task GetStudentCoursesForEvaluation() => Verify(nameof(GetStudentCoursesForEvaluation), [queries[1]]);
	[Fact] public Task SplitDataContext() => Verify(nameof(SplitDataContext), queries);
	[Fact] public Task SplitDataContextRenamed() => Verify(nameof(SplitDataContextRenamed), queries, false);

	[Fact]
	public Task CustomSettingName()
	{
		var name = nameof(CustomSettingName);
		return TestHelper.Verify([
			TestHeader(name, attributes: [name, "AnotherName"], callback: p => $$"""
			[{{QueryAttributeName}}(QueryFiles.{{p}}, "TestDatabaseConnectionString")]
			""")], [$$"""
			--Name: {{name}}
			Declare	@Bob1 varchar(max) = 'Sally';
			Use [Database];
			Select @Bob1;
			"""]);
	}

	[Fact]
	public Task SharedTable()
	{
		var name = nameof(SharedTable);
		return TestHelper.Verify([$$"""
		{{TestHeader(name, [$"{name}1"/*, $"{name}2"*/])}}
		
		//[SQuiLTable(TableType.Bob)]
		//[SQuiLTable(TableType.Sally)]
		public partial class Table {}
		"""], [$$"""
		--Name: {{name}}1
		Declare	@Return_Bob table(ID int);
		Use [Database];
		Select 1;
		"""/*, $$"""
		--Name: {{name}}2
		Declare	@Return_Sally table(ID int);
		Use [Database];
		Select 2;
		"""*/]);
	}

	[Fact]
	public Task TwoQueryDataContext()
	{
		var name = nameof(TwoQueryDataContext);
		return TestHelper.Verify([TestHeader(name, [
				$"{name}1",
			$"{name}2"
				], p => $"""[{QueryAttributeName}(QueryFiles.{p}, setting: "ConnectionString{p}")]""")], [$$"""
			--Name: {{name}}1
			Use BIWarehouse;
			Select 1
			""", $$"""
			--Name: {{name}}2
			
			Declare		@PersonID varchar(10),
						@Debug bit = 1;

			Set	@PersonID = '0300996';

			Declare	@Return_Participation table(
				SectionID varchar(20),
				PersonID varchar(10),
				ProfessorID varchar(10),
				TermCode varchar(10),
				CompletedDate datetime
			);

			Declare	@Return_Override table(
				SectionID varchar(20),
				TermCode varchar(10),
				CourseCode varchar(20),
				BeginDate datetime,
				EndDate datetime
			);

			Use DataRepositoryTest;

			Insert Into @Return_Participation
			Select * From (
			Select		--pv.ElementId As ElementID,
						Max(Iif(pv.PropertyName = 'ParticipationSectionId', PropertyValue, Null)) as SectionID,
						Max(Iif(pv.PropertyName = 'ParticipationStudentId', PropertyValue, Null)) as PersonID,
						Max(Iif(pv.PropertyName = 'ParticipationTeacherId', PropertyValue, Null)) as ProfessorID,
						Max(Iif(pv.PropertyName = 'ParticipationTerm', PropertyValue, Null)) as TermCode,
						Max(Iif(pv.PropertyName = 'ParticipationCompletedOn', Cast(PropertyValue As datetime), Null)) as CompletedDate
			From		CourseEvaluation_PropertyValues pv
						Inner Join (
							Select		ElementId
							From		CourseEvaluation_PropertyValues
							Where		PropertyName = 'Tag'
										And PropertyValue = 'Participation'
						) tags
							On tags.ElementId = pv.ElementId
			Group By	pv.ElementId
			) list
			Where PersonID = @PersonID;

			Insert Into @Return_Override
			Select		Max(Iif(pv.PropertyName = 'SectionDateSectionId', PropertyValue, Null)) as SectionID,
						Max(Iif(pv.PropertyName = 'SectionDateTerm', PropertyValue, Null)) as TermCode,
						Max(Iif(pv.PropertyName = 'SectionDateDescription', PropertyValue, Null)) as CourseCode,
						Max(Iif(pv.PropertyName = 'SectionDateEvaluationBeginDate', Cast(PropertyValue As date), Null)) as BeginDate,
						Max(Iif(pv.PropertyName = 'SectionDateEvaluationEndDate', Cast(PropertyValue As date), Null)) as EndDate
			Fr	om		CourseEvaluation_PropertyValues pv
						Inner Join (
							Select		ElementId
							From		CourseEvaluation_PropertyValues
							Where		PropertyName = 'Tag'
										And PropertyValue = 'SectionDate'
						) tags
							On tags.ElementId = pv.ElementId
			Group By	pv.ElementId

			Select * From @Return_Participation;
			Select * From @Return_Override;

			/*

			ParticipationCompletedOn
			ParticipationSectionId
			ParticipationStudentId
			ParticipationTeacherId
			ParticipationTerm

			*/
			"""]);
	}
	/*
	[Fact]
	public Task ModifyReturnTableWithProperties()
	{
		var name = nameof(ModifyReturnTableWithProperties);
		return TestHelper.Verify([$$"""
			{{TestHeader(name)}}

			"""], [$$"""
			--Name: {{name}}
			
			Declare @Return_Courses table(
				EvalationID varchar(20),
				TermCode varchar(10),
				CourseCode varchar(20),
				CourseTitle varchar(100),
				ProfessorName varchar(100),
				EvaluationStatus varchar(50)
			);

			Use BIWarehouse;

			Insert Into @Return_Courses
			Select		Char(64 + sf.FacultyOrder) + Cast(sf.SectionFacultyID As varchar(10)),
						c.TermCode, c.CourseCode, c.CourseTitle, p.PreferredName, Case
							When @AsOfDate < t.BeginDate Then 'Opens On ' + Format(t.BeginDate, 'dddd, MMMM d')
							When @AsOfDate < t.EndDate Then 'Open Until ' + Format(t.EndDate, 'dddd, MMMM d')
							Else 'Closed On ' + Format(t.EndDate, 'dddd, MMMM d')
						End
			From		adm.SectionFaculty sf
						Inner Join pub.Person p
							On p.PersonID = sf.PersonID
						Inner Join @Courses c
							On c.SectionID = sf.SectionID
						Inner Join @Terms t
							On t.TermCode = c.TermCode;
			"""]);
	}
	*/
	[Fact]
	public Task InheritedSimpleBaseWithPrimaryContructedParameters()
	{
		var name = nameof(InheritedSimpleBaseWithPrimaryContructedParameters);
		return TestHelper.Verify([$$"""
			{{TestHeader(name)}}
			
			public partial record {{name}}DataContextQueriesExampleRequest : BaseRecord
			{
				public int Sally1 { get; set; }
			}
			
			public partial record BaseRecord(string Bob1)
			{
			}
			"""], [$$"""
			--Name: {{name}}
			Declare	@Bob1 varchar(max) = 'Sally';
			Use [Database];
			Select @Bob1;
			"""]);
	}

	private static (string Name, string[] Attributes) Format(string file)
	{
		var name = file.Replace("\\", "");
		return (name, [$"[{QueryAttributeName}(QueryFiles.{name})]"]);
	}

	private static Task Verify(string name, IEnumerable<string> files, bool useGenericName = true)
		=> TestHelper.Verify(
			files.Select(Format).Select(p
				=> GetSource(p.Attributes, useGenericName ? "ApplicationSpecific" : p.Name)),
			files.Select(p => $"{p}.sql"));

	private static string TestHeader(string name,
		IEnumerable<string> attributes = default!,
		Func<string, string> callback = default!)
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
			public partial class {{name}}DataContext(
				IConfiguration Configuration)
				: {{BaseDataContextClassName}}(Configuration)
			{
			}
			""";
	}

	private static string GetSource(IEnumerable<string> attributes, string name) => $$"""
		{{TestHeader(name, attributes)}}

		public partial record ApplicationSpecificDataContextQueriesExampleRequest() : BaseRecord
		{
			public int Sally1 { get; set; }
		}

		public partial record ApplicationSpecificDataContextQueriesExampleRequestSallyTable() : BaseRecord {}

		public record BaseRecord : AnotherBaseRecord
		{
			public bool Bob { get; init; }		
			public bit Sally2 { get; set; }
		}

		public record AnotherBaseRecord : BaseRecordPrimaryConstructor("test")
		{
			public string Bob2 { get; set; }
		}

		public record BaseRecordPrimaryConstructor(string Bob1);
		""";
}