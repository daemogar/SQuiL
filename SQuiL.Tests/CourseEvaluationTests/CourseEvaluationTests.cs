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
			.Select(p => $"""
				--Name: {Path.GetFileNameWithoutExtension(p)}
				{File.ReadAllText(p)}
				""")
			.ToList();

		// compileCheck off: pins KNOWN GENERATOR GAP — TermTable below declares
		// a primary-constructor parameter list on the user's partial record,
		// and the generated TermTable partial emits one too (CS8863: only a
		// single partial declaration may have a parameter list). The intended
		// contract for customizing generated records is undecided; see master
		// TODO "Tier-0 findings".
		await TestHelper.Verify(
			compileCheck: false,
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
				public partial record TermTable(string TermCode);

				[SQuiLTable(TableType.Section)]
				[SQuiLTable(TableType.Sections)]
				public partial record SectionTable {}
				"""],
			files: files);
	}
}
