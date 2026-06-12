using SQuiL.SourceGenerator.Parser;

using static SQuiL.SourceGenerator.Parser.SQuiLVariableValidator;

namespace SQuiL.Tests;

/// <summary>
/// Unit tests for <see cref="SQuiLVariableValidator"/> — the rule that a SQuiL file
/// must be valid T-SQL: every @variable reference needs a textually-preceding
/// DECLARE (no remapping, no implicit specials), and @Debug/@EnvironmentName must
/// be declared before the USE statement, preferably first.
/// </summary>
public class VariableValidatorTests
{
	[Fact]
	public void Valid_file_has_no_findings()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Declare @Param_Name varchar(100);
			Declare @Return_Count int;
			Use MyDatabase;
			Set @Return_Count = (Select Count(*) From Users Where Name = @Param_Name);
			Select @Return_Count;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Reference_never_declared_is_flagged()
	{
		var findings = Validate("""
			Declare @Param_PersonID varchar(10);
			Use MyDatabase;
			Select * From People Where PersonID = @PersonID;
			""");

		var finding = Assert.Single(findings);
		Assert.Equal(FindingKind.Undeclared, finding.Kind);
		Assert.Equal("@PersonID", finding.Name);
		Assert.Equal(3, finding.Line);
	}

	[Fact]
	public void Reference_before_declaration_is_flagged_as_used_before_declared()
	{
		var findings = Validate("""
			Set @Param_Name = 'x';
			Declare @Param_Name varchar(100);
			Use MyDatabase;
			""");

		var finding = Assert.Single(findings);
		Assert.Equal(FindingKind.UsedBeforeDeclared, finding.Kind);
		Assert.Equal("@Param_Name", finding.Name);
		Assert.Equal(1, finding.Line);
	}

	[Fact]
	public void Debug_and_EnvironmentName_require_declaration_like_any_variable()
	{
		var findings = Validate("""
			Declare @Param_Name varchar(100);
			Use MyDatabase;
			If @Debug = 1 Select @EnvironmentName;
			""");

		Assert.Equal(2, findings.Count);
		Assert.All(findings, f => Assert.Equal(FindingKind.Undeclared, f.Kind));
		Assert.Equal("@Debug", findings[0].Name);
		Assert.Equal("@EnvironmentName", findings[1].Name);
	}

	[Fact]
	public void Declared_special_variables_are_valid_references()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Declare @EnvironmentName varchar(50);
			Declare @Return_Count int;
			Use MyDatabase;
			If @Debug = 1 Select @EnvironmentName;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Special_declared_after_use_is_flagged()
	{
		var findings = Validate("""
			Declare @Return_Count int;
			Use MyDatabase;
			Declare @Debug bit = 0;
			If @Debug = 1 Select 1;
			""");

		var finding = Assert.Single(findings);
		Assert.Equal(FindingKind.SpecialAfterUse, finding.Kind);
		Assert.Equal("@Debug", finding.Name);
		Assert.Equal(3, finding.Line);
	}

	[Fact]
	public void Special_declared_after_other_header_declarations_is_flagged_not_first()
	{
		var findings = Validate("""
			Declare @Param_Name varchar(100);
			Declare @Debug bit = 0;
			Use MyDatabase;
			""");

		var finding = Assert.Single(findings);
		Assert.Equal(FindingKind.SpecialNotFirst, finding.Kind);
		Assert.Equal("@Debug", finding.Name);
		Assert.Equal(2, finding.Line);
	}

	[Fact]
	public void Both_specials_first_in_any_order_are_fine()
	{
		var findings = Validate("""
			Declare @EnvironmentName varchar(50);
			Declare @Debug bit = 0;
			Declare @Param_Name varchar(100);
			Use MyDatabase;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Multi_variable_declare_with_defaults_declares_all_names()
	{
		var findings = Validate("""
			Declare @Param_Debug bit = 1,
					@Param_PersonID varchar(10),
					@Param_CourseCode varchar(20) = Null;
			Use MyDatabase;
			Select @Param_Debug, @Param_PersonID, @Param_CourseCode;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Reference_inside_declare_default_expression_is_validated()
	{
		var findings = Validate("""
			Declare @Params_Terms table(TermCode varchar(10));
			Use MyDatabase;
			Declare @POS int = (Select POS From @Terms Where IsCurrent = 1);
			""");

		var finding = Assert.Single(findings);
		Assert.Equal(FindingKind.Undeclared, finding.Kind);
		Assert.Equal("@Terms", finding.Name);
	}

	[Fact]
	public void Table_declare_without_semicolon_followed_by_statement_works()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Use MyDatabase;
			Begin
				Declare @Courses table(
					SectionID varchar(20),
					Credits decimal(10,4))
				Insert Into @Courses
				Select SectionID, Credits From Sections
			End;
			Select * From @Courses;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void System_variables_strings_comments_and_brackets_are_ignored()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Use MyDatabase;
			-- comment mentions @NotReal1
			/* block comment @NotReal2 /* nested @NotReal3 */ still comment */
			Select 'literal @NotReal4' As [@NotReal5], @@ROWCOUNT;
			""");

		Assert.True(findings.Count == 0,
			string.Join("; ", findings.Select(f => $"{f.Kind} {f.Name} @{f.Line}:{f.Column}")));
	}

	[Fact]
	public void Variable_names_are_case_insensitive()
	{
		var findings = Validate("""
			Declare @Param_Name varchar(100);
			Use MyDatabase;
			Select @PARAM_NAME;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Every_occurrence_of_an_undeclared_reference_is_reported()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Use MyDatabase;
			Select @Missing;
			Select @Missing;
			""");

		Assert.Equal(2, findings.Count);
		Assert.All(findings, f => Assert.Equal("@Missing", f.Name));
	}

	[Fact]
	public void Case_expression_in_declare_default_does_not_end_the_declare_list()
	{
		var findings = Validate("""
			Declare @Debug bit = 0;
			Use MyDatabase;
			Declare @A int = Case When 1 = 1 Then 1 Else 0 End, @B int = @A;
			Select @A, @B;
			""");

		Assert.Empty(findings);
	}

	[Fact]
	public void Column_and_position_are_reported()
	{
		var findings = Validate("Use MyDatabase;\r\nSelect @X;");

		var finding = Assert.Single(findings);
		Assert.Equal(2, finding.Line);
		Assert.Equal(8, finding.Column);
	}

	[Fact]
	public void Fixed_real_example_files_validate_clean()
	{
		var directory = Path.Combine("..", "..", "..", "CourseEvaluationTests");
		foreach (var file in Directory.GetFiles(directory, "*.sql"))
		{
			var findings = Validate(File.ReadAllText(file))
				.Where(f => f.Kind is FindingKind.Undeclared or FindingKind.UsedBeforeDeclared)
				.ToList();
			Assert.True(findings.Count == 0,
				$"{Path.GetFileName(file)}: {string.Join("; ", findings.Select(f => $"{f.Kind} {f.Name} @{f.Line}:{f.Column}"))}");
		}
	}
}
