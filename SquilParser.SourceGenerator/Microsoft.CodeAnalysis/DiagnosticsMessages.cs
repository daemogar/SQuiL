using System.Text.RegularExpressions;

namespace Microsoft.CodeAnalysis;

public static class DiagnosticsMessages
{
  public static void CriticalGenerationFailure(this SourceProductionContext context, Exception e)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0014", "Critical Generation Failure", e.Message + " " + e.StackTrace.Replace("\r", "").Replace("\t", "").Replace("\n", " ")));
  }

  public static void FileNotFound(this SourceProductionContext context, string filepath, Location location)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0001", "File Not Found", $"Cannot locate the file: {filepath}", location));
  }

  public static void MissingPartialDeclaration(this SourceProductionContext context, Location location)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0006", "Missing Partial", $"Add the partial keyword to the class definition", location));
  }

  public static void MissingBaseDataContextDeclaration(this SourceProductionContext context, Location location)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0010", $"Missing {SourceGeneratorHelper.BaseDataContextClassName}", $"DataContext must inherit from {SourceGeneratorHelper.BaseDataContextClassName}", location));
  }

  public static void DirectoryNotFound(this SourceProductionContext context, string message, Location location)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0002", "Directory Not Found", message, location));
  }

  public static void ReportNoMicrosoftDataSqlClientDll(this SourceProductionContext context)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0007", "Microsoft.Data.SqlClient.DLL Missing",
		"Add Microsoft.Data.SqlClient from NuGet to project.", Location.None));
  }

  public static void ReportNoMicrosoftExtensionsConfigurationDll(this SourceProductionContext context)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0008", "Microsoft.Extensions.Configuration.DLL Missing",
		"Add Microsoft.Extensions.Configuration from NuGet to project.", Location.None));
  }

  public static void ReportNoMicrosoftExtensionsDependencyInjectionDll(this SourceProductionContext context)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0009", "Microsoft.Extensions.DependencyInjection.DLL Missing",
		"Add Microsoft.Extensions.DependencyInjection from NuGet to project.", Location.None));
  }

  [Obsolete("Not Used", error: true)]
  public static void SQuiLProjectRootNotSet(this SourceProductionContext context)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0003", "SQuiL Project Root Not Set", $"""
			Project csproj file is missing the SQuiLParserRoot configuration directive. Add the following:
			
			<PropertyGroup>
				<{SourceGeneratorHelper.ConfigRootPath}>$(SolutionDir)</{SourceGeneratorHelper.ConfigRootPath}>
			</PropertyGroup>

			<ItemGroup>
			   <CompilerVisibleProperty Include="{SourceGeneratorHelper.ConfigRootPath}" />
			</ItemGroup>
			"""));
  }

  public static void SQuiLClassContextMustHaveNamespace(this SourceProductionContext context, string className, Location location)
  {

	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0004", "SQuiL Class Missing Namespace",
		$"The class `{className}` cannot be a top-level class. Add a namespace to the class.", location));
  }

  public static void Debug(this SourceProductionContext context, string message)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0000", "Development Data", message, category: "Logger"));
  }

  public static void ReportLexicalParseErrorDiagnostic(
	  this SourceProductionContext context,
	  DiagnosticException exception, string filename)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP1001",
		"Failed Parsing SQuiL File", exception.Message, exception.GetLocation(filename)));
  }

  public static void ReportNoDataContextUsage(this SourceProductionContext context)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0005", "No SQuiL Contexts Are Created",
		$"There are no SQuiL contexts created. Make sure to create a partial class and add the [{SourceGeneratorHelper.AttributeName}]."));
  }

  private static readonly Regex Newline = new("(\r?\n)", RegexOptions.Compiled | RegexOptions.Singleline);

  public static void ReportMissingStatement(this SourceProductionContext context, Exception exception)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0011", "Missing Query Statement Component", exception.Message));
  }

  public static void ReportMissingKeywordModifier(this SourceProductionContext context, Exception exception)
  {
	context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0012", "Missing or Invalid Keyword Modifier", exception.Message));
  }

  private static Diagnostic CreateDiagnostic(DiagnosticSeverity severity, string id, string title, string message, Location? location = default, string category = "Design", string? description = default)
  {
	return Diagnostic.Create(new DiagnosticDescriptor(id, title,
		Newline.Replace(message, " "), category, severity, isEnabledByDefault: true, description),
		location ?? Location.None);
  }
}
