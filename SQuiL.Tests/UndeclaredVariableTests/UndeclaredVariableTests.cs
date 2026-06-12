using System.Runtime.CompilerServices;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Tests;

/// <summary>
/// End-to-end generator tests for the SP0013 (undeclared variable) and SP0016
/// (special variable placement) diagnostics — a SQuiL file must be valid T-SQL,
/// so referencing a variable without a preceding DECLARE fails the build.
/// </summary>
public class UndeclaredVariableTests
{
	private static string TestHeader([CallerMemberName] string name = default!)
		=> $$"""
			using Microsoft.Extensions.Configuration;
			using {{NamespaceName}};

			namespace TestCase;

			[{{QueryAttributeName}}(QueryFiles.{{name}})]
			public partial class {{name}}DataContext(IConfiguration Configuration) : {{BaseDataContextClassName}}(Configuration)
			{
			}
			""";

	[Fact]
	public Task UndeclaredReferenceFailsBuild()
	{
		var name = nameof(UndeclaredReferenceFailsBuild);
		return TestHelper.Verify([TestHeader()], [$$"""
			--Name: {{name}}
			Declare @Param_PersonID varchar(10);
			Declare @Return_Count int;
			Use [Database];
			Set @Return_Count = (Select Count(*) From People Where PersonID = @PersonID);
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task ErrorAndErrorsAreNotInterchangeable()
	{
		// Declaring @Error (singular) does NOT satisfy a reference to @Errors
		// (plural) — the parser no longer cross-matches them, and SP0013 flags
		// the mismatch as an undeclared variable.
		var name = nameof(ErrorAndErrorsAreNotInterchangeable);
		return TestHelper.Verify([TestHeader()], [$$"""
			--Name: {{name}}
			Declare @Return_Count int;
			Declare @Error table(
				Number int,
				Severity int,
				[State] int,
				Line int,
				[Procedure] varchar(max),
				[Message] varchar(max)
			);
			Use [Database];
			Insert Into @Errors;
			Select @Return_Count;
			"""]);
	}

	[Fact]
	public Task SpecialVariablePlacementIsEnforced()
	{
		var name = nameof(SpecialVariablePlacementIsEnforced);
		return TestHelper.Verify([TestHeader()], [$$"""
			--Name: {{name}}
			Declare @Param_Name varchar(100);
			Declare @Debug bit = 0;
			Declare @Return_Count int;
			Use [Database];
			Declare @EnvironmentName varchar(50);
			Set @Return_Count = 1;
			Select @Return_Count;
			"""]);
	}
}
