using System.Text.RegularExpressions;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Extension methods on <see cref="SourceProductionContext"/> for every diagnostic the
/// generator can emit.  Each method maps to a specific SP-prefixed diagnostic ID and
/// encapsulates the severity, title, and message format.
/// </summary>
public static class DiagnosticsMessages
{
	/// <summary>Regex used to collapse newlines to spaces inside diagnostic messages.</summary>
	public static readonly Regex Newline = new("(\r?\n)", RegexOptions.Compiled | RegexOptions.Singleline);

	/// <summary>SP0014 — An unhandled exception escaped the top-level generation pipeline.</summary>
	public static void CriticalGenerationFailure(this SourceProductionContext context, Exception e)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0014", "Critical Generation Failure", e.Message + " " + e.StackTrace.Replace("\r", "").Replace("\t", "").Replace("\n", " ")));
	}

	/// <summary>SP0001 — The <c>.sql</c> additional file referenced by a <c>[SQuiLQueryAttribute]</c> could not be located.</summary>
	public static void FileNotFound(this SourceProductionContext context, string filepath, Location location)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0001", "File Not Found", $"Cannot locate the file: {filepath}", location));
	}

	/// <summary>SP0006 — The data-context class is missing the <c>partial</c> keyword.</summary>
	public static void MissingPartialDeclaration(this SourceProductionContext context, Location location)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0006", "Missing Partial", $"Add the partial keyword to the class definition", location));
	}

	/// <summary>SP0010 — The data-context class does not inherit from <see cref="SourceGeneratorHelper.BaseDataContextClassName"/>.</summary>
	public static void MissingBaseDataContextDeclaration(this SourceProductionContext context, Location location)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0010", $"Missing {SourceGeneratorHelper.BaseDataContextClassName}", $"DataContext must inherit from {SourceGeneratorHelper.BaseDataContextClassName}", location));
	}

	/// <summary>SP0002 — A required directory was not found.</summary>
	public static void DirectoryNotFound(this SourceProductionContext context, string message, Location location)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0002", "Directory Not Found", message, location));
	}

	/// <summary>SP0015 — The same table name has been mapped to more than one C# class via <c>[SQuiLTableAttribute]</c>.</summary>
	public static void ReportDuplicateTableMap(this SourceProductionContext context, Dictionary<string, List<(string Attribute, Location Location)>> issues)
	{
		foreach (var issue in issues)
			foreach (var (classname, location) in issue.Value)
			{
				context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0015", "Duplicate use of table mapping.",
					$"{issue.Key} was already defined on {classname}.", location));
			}
	}

	/// <summary>SP0007 — <c>Microsoft.Data.SqlClient</c> is not referenced by the consumer project.</summary>
	public static void ReportNoMicrosoftDataSqlClientDll(this SourceProductionContext context)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0007", "Microsoft.Data.SqlClient.DLL Missing",
			"Add Microsoft.Data.SqlClient from NuGet to project.", Location.None));
	}

	/// <summary>SP0008 — <c>Microsoft.Extensions.Configuration</c> is not referenced by the consumer project.</summary>
	public static void ReportNoMicrosoftExtensionsConfigurationDll(this SourceProductionContext context)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0008", "Microsoft.Extensions.Configuration.DLL Missing",
			"Add Microsoft.Extensions.Configuration from NuGet to project.", Location.None));
	}

	/// <summary>SP0009 — <c>Microsoft.Extensions.DependencyInjection</c> is not referenced by the consumer project.</summary>
	public static void ReportNoMicrosoftExtensionsDependencyInjectionDll(this SourceProductionContext context)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0009", "Microsoft.Extensions.DependencyInjection.DLL Missing",
			"Add Microsoft.Extensions.DependencyInjection from NuGet to project.", Location.None));
	}

	/// <summary>SP0003 — The project is missing the <c>SQuiLSolutionRoot</c> MSBuild property. <b>Obsolete — no longer used.</b></summary>
	[Obsolete("Not Used", error: true)]
	public static void SQuiLProjectRootNotSet(this SourceProductionContext context)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0003", "SQuiL Project Root Not Set", $"""
			Project csproj file is missing the SQuiLRoot configuration directive. Add the following:

			<PropertyGroup>
				<{SourceGeneratorHelper.ConfigRootPath}>$(SolutionDir)</{SourceGeneratorHelper.ConfigRootPath}>
			</PropertyGroup>

			<ItemGroup>
			   <CompilerVisibleProperty Include="{SourceGeneratorHelper.ConfigRootPath}" />
			</ItemGroup>
			"""));
	}

	/// <summary>SP0004 — A class decorated with <c>[SQuiLQueryAttribute]</c> is not inside a namespace.</summary>
	public static void SQuiLClassContextMustHaveNamespace(this SourceProductionContext context, string className, Location location)
	{

		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0004", "SQuiL Class Missing Namespace",
			$"The class `{className}` cannot be a top-level class. Add a namespace to the class.", location));
	}

	/// <summary>SP0000 — Debug/development warning that emits an intermediate parse value during generation.</summary>
	public static void Debug(this SourceProductionContext context, string message)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0000", "Development Data", message, category: "Logger"));
	}

	/// <summary>SP1001 — The tokenizer or parser threw a <see cref="DiagnosticException"/> while processing a SQL file.</summary>
	public static void ReportLexicalParseErrorDiagnostic(
		this SourceProductionContext context,
		DiagnosticException exception, string filename)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP1001",
			"Failed Parsing SQuiL File", exception.Message, exception.GetLocation(filename)));
	}

	/// <summary>SP0005 — No classes decorated with <c>[SQuiLQueryAttribute]</c> were found in the project.</summary>
	public static void ReportNoDataContextUsage(this SourceProductionContext context)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0005", "No SQuiL Contexts Are Created",
			$"There are no SQuiL contexts created. Make sure to create a partial class and add the [{SourceGeneratorHelper.QueryAttributeName}]."));
	}

	/// <summary>SP0011 — A required component of the query (e.g. a USE or BODY block) could not be generated.</summary>
	public static void ReportMissingStatement(this SourceProductionContext context, Exception exception)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0011", "Missing Query Statement Component", exception.Message));
	}

	/// <summary>SP0012 — A SQL variable uses an unsupported access modifier (e.g. <c>private</c> or <c>internal</c>).</summary>
	public static void ReportMissingKeywordModifier(this SourceProductionContext context, Exception exception)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0012", "Missing or Invalid Keyword Modifier", exception.Message));
	}

	/// <summary>
	/// SP0013 — A variable is referenced without a textually-preceding <c>DECLARE</c>.
	/// SQuiL files must be valid T-SQL; SQL Server rejects the whole batch at compile
	/// time for undeclared variables, so the build fails too. No variable is exempt —
	/// <c>@Debug</c> and <c>@EnvironmentName</c> must be declared like any other.
	/// </summary>
	public static void ReportUndeclaredVariable(this SourceProductionContext context, string filename, SQuiL.SourceGenerator.Parser.SQuiLVariableValidator.Finding finding)
	{
		var detail = finding.Kind == SQuiL.SourceGenerator.Parser.SQuiLVariableValidator.FindingKind.UsedBeforeDeclared
			? "before its declaration. Move the Declare above the first use."
			: "but never declared. SQuiL files must be valid T-SQL — declare it before use.";

		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0013", "Undeclared Variable",
			$"{filename}: variable `{finding.Name}` is referenced (line {finding.Line}, column {finding.Column}) {detail}"));
	}

	/// <summary>
	/// SP0016 — <c>@Debug</c>/<c>@EnvironmentName</c> is declared in the wrong place:
	/// after the <c>USE</c> statement (error — it must be part of the header), or after
	/// other header declarations (warning — it should be declared first).
	/// </summary>
	public static void ReportSpecialVariablePlacement(this SourceProductionContext context, string filename, SQuiL.SourceGenerator.Parser.SQuiLVariableValidator.Finding finding)
	{
		var afterUse = finding.Kind == SQuiL.SourceGenerator.Parser.SQuiLVariableValidator.FindingKind.SpecialAfterUse;

		context.ReportDiagnostic(CreateDiagnostic(
			afterUse ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
			"SP0016", "Special Variable Placement",
			afterUse
				? $"{filename}: `{finding.Name}` (line {finding.Line}, column {finding.Column}) must be declared before the Use statement."
				: $"{filename}: `{finding.Name}` (line {finding.Line}, column {finding.Column}) should be declared at the top of the header, before other declarations."));
	}

	/// <summary>
	/// SP0017 — Declarations that share one generated record type (same table name, or
	/// different names mapped to one class via <c>[SQuiLTableAttribute]</c>) declare
	/// different column shapes. The shared record's positional constructor cannot serve
	/// mismatched shapes, so the record is not emitted.
	/// </summary>
	public static void ReportTableShapeMismatch(this SourceProductionContext context, List<(string TableName, string Expected, string Actual)> issues)
	{
		foreach (var (table, expected, actual) in issues)
			context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0017", "Table Shape Mismatch",
				$"All declarations that generate the record `{table}` must declare identical columns " +
				$"(same names, types, nullability, and order). Found {expected} and {actual}. " +
				"Rename one of the variables or align the column lists."));
	}

	/// <summary>
	/// SP0018 — A <c>[SQuiLTableAttribute]</c> partial record declares a primary constructor.
	/// The generator owns the parameter list (only a single partial declaration may have one),
	/// so the user partial must use a body instead.
	/// </summary>
	public static void ReportTableRecordPrimaryConstructor(this SourceProductionContext context, string recordName, Location location)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0018", "Table Record Primary Constructor",
			$"The [{SourceGeneratorHelper.TableTypeAttributeName}] partial record `{recordName}` must not declare a primary constructor — " +
			$"the generator emits the parameter list. Replace `record {recordName}(...)` with `record {recordName} {{ }}`.", location));
	}

	/// <summary>
	/// SP0019 — <c>@SuppressDebug</c> is declared without a <c>@Debug</c> declaration in the same file.
	/// <c>@SuppressDebug</c> only has meaning alongside <c>@Debug</c> (it gates the auto-debug
	/// expression), so declaring it alone is a build error.
	/// </summary>
	public static void ReportSuppressDebugWithoutDebug(this SourceProductionContext context, string filename, SQuiL.SourceGenerator.Parser.SQuiLVariableValidator.Finding finding)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0019", "SuppressDebug Requires Debug",
			$"{filename}: `{finding.Name}` (line {finding.Line}, column {finding.Column}) may only be declared when `@Debug` is also declared in the same file."));
	}

	/// <summary>
	/// Builds a <see cref="Diagnostic"/> with newlines removed from the message so IDEs display it on one line.
	/// </summary>
	private static Diagnostic CreateDiagnostic(DiagnosticSeverity severity, string id, string title, string message, Location? location = default, string category = "Design", string? description = default)
	{
		return Diagnostic.Create(new DiagnosticDescriptor(id, title,
			Newline.Replace(message, " "), category, severity, isEnabledByDefault: true, description),
			location ?? Location.None);
	}
}
