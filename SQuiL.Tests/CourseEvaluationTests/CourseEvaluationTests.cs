using System.Diagnostics;

namespace SQuiL.Tests.CourseEvaluationTests;

public class CourseEvaluationTests
{
	[Fact]
	public async Task RealExample()
	{
		var path = Path.GetDirectoryName(typeof(CourseEvaluationTests).Assembly.Location);
		var sourceDir = Path.Combine(path!, "..", "..", "..", nameof(CourseEvaluationTests));
		var files = Directory
			.GetFiles(sourceDir, "*.sql", SearchOption.AllDirectories)
			// Directory enumeration order is OS-dependent (Windows returns sorted,
			// Linux does not); sort so the test feeds files deterministically.
			.OrderBy(p => p, StringComparer.Ordinal)
			.Select(p => $"""
				--Name: {Path.GetFileNameWithoutExtension(p)}
				{File.ReadAllText(p)}
				""")
			.ToList();

		await TestHelper.Verify(
			sources: ["""
				using Microsoft.Extensions.Configuration;

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
				public partial record TermTable {}

				[SQuiLTable(TableType.Section)]
				[SQuiLTable(TableType.Sections)]
				public partial record SectionTable {}
				"""],
			files: files);
	}
}
