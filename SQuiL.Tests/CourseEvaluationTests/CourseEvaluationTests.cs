using System.Diagnostics;

namespace SQuiL.Tests.CourseEvaluationTests;

public class CourseEvaluationTests
{
	[Fact]
	public async Task RealExample()
	{
		var path = Path.GetDirectoryName(typeof(CourseEvaluationTests).Assembly.Location);
		var files = Directory
			.GetFiles(path!, $"..\\..\\..\\{nameof(CourseEvaluationTests)}\\*.sql", SearchOption.AllDirectories)
			.Select(p => $"""
				--Name: {p[p.LastIndexOf('\\')..][1..^4]}
				{File.ReadAllText(p)}
				""")
			.ToList();

		await TestHelper.Verify(
			["""
				using SQuiL;

				using System.Data;

				namespace CourseEvaluation.Application.Data;

				[SQuiLQuery(QueryFiles.GetCourseForEvaluationByEvaluationID, "Warehouse")]
				[SQuiLQuery(QueryFiles.GetActiveTermsForStudentEvaluations, "Warehouse")]
				[SQuiLQuery(QueryFiles.GetStudentCoursesForEvaluationByTerm, "Warehouse")]
				[SQuiLQuery(QueryFiles.GetStudentParticipationAndSectionOverrides, "DataRepository")]
				[SQuiLQuery(QueryFiles.GetSectionDetails, "Warehouse")]
				[SQuiLQuery(QueryFiles.GetQuestionsForEvaluation, "DataRepository")]
				public partial class CourseEvaluationDataContext(IConfiguration Configuration) : SQuiLBaseDataContext(Configuration)
				{
				}
				
				[SQuiLTable(TableType.Terms)]
				public partial record TermTable(string TermCode);

				[SQuiLTable(TableType.Section)]
				[SQuiLTable(TableType.Sections)]
				public partial record SectionTable {}
				"""],
			files);
	}
}
