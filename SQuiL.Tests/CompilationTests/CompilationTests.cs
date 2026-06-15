namespace SQuiL.Tests;

/// <summary>
/// Tier-0 compliance: the generator's output must actually compile when
/// combined with the user's sources and the real runtime references —
/// a Verify snapshot alone stays green for output that doesn't build.
/// </summary>
public class CompilationTests : BaseTest
{
	[Fact]
	public void MinimalQueryGeneratedCodeCompiles()
	{
		CompilationAssert.GeneratedCodeCompiles(
			[TestHeader([nameof(MinimalQueryGeneratedCodeCompiles)])],
			[$"""
			--Name: {nameof(MinimalQueryGeneratedCodeCompiles)}
			Declare @Param_PersonID varchar(10);
			Declare @Return_Count int;
			Use [Database];
			Set @Return_Count = (Select Count(*) From People Where PersonID = @Param_PersonID);
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public void TableShapesGeneratedCodeCompiles()
	{
		CompilationAssert.GeneratedCodeCompiles(
			[TestHeader([nameof(TableShapesGeneratedCodeCompiles)])],
			[$"""
			--Name: {nameof(TableShapesGeneratedCodeCompiles)}
			Declare @Params_People table(PersonID varchar(10), Age int Null);
			Declare @Returns_Matches table(PersonID varchar(10), Score decimal(10,4));
			Use [Database];
			Insert Into @Returns_Matches
			Select PersonID, 1.0 From @Params_People;
			Select * From @Returns_Matches;
			"""]);
	}

	[Fact]
	public void GeneratedCodeCompilesWithoutImplicitUsings()
	{
		// User source carries its own explicit usings so the only failures come from
		// generated code that relies on bare System.* names without emitting usings.
		var userSource = """
			using Microsoft.Extensions.Configuration;
			using SQuiL;

			namespace TestCase;

			[SQuiLQueryAttribute(QueryFiles.GeneratedCodeCompilesWithoutImplicitUsings)]
			public partial class GeneratedCodeCompilesWithoutImplicitUsingsDataContext(IConfiguration Configuration) : SQuiLBaseDataContext(Configuration)
			{
			}
			""";

		CompilationAssert.GeneratedCodeCompiles(
			[userSource],
			[$"""
			--Name: {nameof(GeneratedCodeCompilesWithoutImplicitUsings)}
			Declare @Param_PersonID varchar(10);
			Declare @Return_Count int;
			Use [Database];
			Set @Return_Count = (Select Count(*) From People Where PersonID = @Param_PersonID);
			Select @Return_Count;
			"""],
			injectImplicitUsings: false);
	}

	[Fact]
	public void ReportsErrorsWhenCombinedCompilationIsBroken()
	{
		var broken = """
			namespace TestCase;
			public class Broken { public UndefinedType Property { get; set; } }
			""";

		var exception = Assert.ThrowsAny<Exception>(() =>
			CompilationAssert.GeneratedCodeCompiles(
				[TestHeader([nameof(ReportsErrorsWhenCombinedCompilationIsBroken)]), broken],
				[$"""
				--Name: {nameof(ReportsErrorsWhenCombinedCompilationIsBroken)}
				Declare @Return_Scaler int;
				Use [Database];
				Set @Return_Scaler = 42;
				Select @Return_Scaler;
				"""]));

		Assert.Contains("UndefinedType", exception.Message);
	}
}
