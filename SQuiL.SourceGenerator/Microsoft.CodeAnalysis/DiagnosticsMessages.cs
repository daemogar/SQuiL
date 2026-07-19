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
	public static void ReportTableShapeMismatch(this SourceProductionContext context, List<(string TableName, string Expected, string Actual, string FirstSourceName, int FirstSourceLine)> issues)
	{
		foreach (var (table, expected, actual, firstSourceName, firstSourceLine) in issues)
		{
			// Point the developer at the EARLIER declaration. Real Roslyn Locations are not
			// available for AdditionalText SQL files, so surface a navigable file+line in the
			// message text instead: "<query> (line N)" when both are known, "line N" when only
			// the line is (same-file conflict), and nothing on the cross-query merge path.
			var hasName = !string.IsNullOrEmpty(firstSourceName);
			var hasLine = firstSourceLine > 0;
			var firstDeclaredIn =
				hasName && hasLine ? $" ↳ first declared at {firstSourceName} (line {firstSourceLine})."
				: hasName ? $" ↳ first declared in: {firstSourceName}."
				: hasLine ? $" ↳ first declared at line {firstSourceLine}."
				: "";
			context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0017", "Table Shape Mismatch",
				$"All declarations that generate the record `{table}` must declare identical columns " +
				$"(same names, types, nullability, and order). Found {expected} and {actual}.{firstDeclaredIn} " +
				"Rename one of the variables or align the column lists."));
		}
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
	/// SP0021 — A single generated row record is shared by more than one context whose
	/// <c>[SQuiLQuery(..., Namespace: ...)]</c> declarations resolve to DIFFERENT namespaces.
	/// The record can only live in one namespace; align the <c>Namespace</c> segments.
	/// </summary>
	public static void ReportRecordNamespaceConflict(this SourceProductionContext context, List<(string TableName, string First, string Second)> issues)
	{
		foreach (var (table, first, second) in issues)
			context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0021", "Record Namespace Conflict",
				$"The shared record `{table}` is placed in conflicting namespaces `{first}` and `{second}` " +
				"by different [SQuiLQuery] Namespace declarations. Use the same Namespace segment for every context that shares this record."));
	}

	/// <summary>
	/// SP0027 — A <c>QueryFiles</c> member is registered by more than one data context.
	/// SQuiL requires a one-to-one query-file → data-context mapping so the file resolves
	/// to exactly one attribute.
	/// </summary>
	public static void ReportDuplicateQueryMapping(
		this SourceProductionContext context, string member, Location? location = default)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0027",
			"Duplicate Query Mapping",
			$"The query file `{member}` is registered by more than one data context. " +
			$"A query file maps to exactly one data context — remove one of the registrations.",
			location));
	}

	/// <summary>
	/// SP0029 — A class declares both <c>[SQuiLQuery]</c> and <c>[SQuiLQueryTransaction]</c>.
	/// Use exactly one — <c>[SQuiLQueryTransaction]</c> already implies a query.
	/// </summary>
	public static void ReportConflictingQueryAttributes(
		this SourceProductionContext context, string className, Location? location = default)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0029",
			"Conflicting Query Attributes",
			$"`{className}` declares both [SQuiLQuery] and [SQuiLQueryTransaction]. " +
			$"Use exactly one — [SQuiLQueryTransaction] already implies a query.",
			location));
	}

	/// <summary>
	/// SP0022 — Within one file, a base name is declared as BOTH a table (list) and a
	/// single object on the same side (both inputs or both outputs). The two declarations
	/// resolve to one request/response property; the generator keeps the first and silently
	/// drops the rest. Each dropped (second-and-later) declaration is a build error.
	/// </summary>
	public static void ReportCardinalityCollision(this SourceProductionContext context, string filename, SQuiL.SourceGenerator.Parser.SQuiLCardinalityValidator.Finding finding)
	{
		static string Raw(string name, bool isOutput, bool isTable)
			=> (isOutput ? (isTable ? "@Returns_" : "@Return_") : (isTable ? "@Params_" : "@Param_")) + name;

		var droppedRaw = Raw(finding.Name, finding.IsOutput, finding.DroppedIsTable);
		var firstRaw = Raw(finding.Name, finding.IsOutput, finding.FirstIsTable);
		var droppedKind = finding.DroppedIsTable ? "a table" : "a single object";
		var firstKind = finding.FirstIsTable ? "a table" : "a single object";

		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0022", "Cardinality Collision",
			$"{filename}: `{droppedRaw}` (line {finding.DroppedLine}) declares `{finding.Name}` as {droppedKind}, " +
			$"but `{firstRaw}` (line {finding.FirstLine}) already declares it as {firstKind}. " +
			"One cardinality wins and the other is silently dropped — rename one variable, or use the same cardinality for both."));
	}

	/// <summary>
	/// SP0030 — Within one query file, two <c>@Return</c>/<c>@Returns</c> outputs have an
	/// identical ordered canonical signature (same column names, order, and C# types — length
	/// and precision do not differentiate). The result sets cannot be routed apart at runtime.
	/// All colliding declarations are flagged, each cross-referencing the other.
	/// </summary>
	public static void ReportShapeCollision(
		this SourceProductionContext context,
		string filename,
		SQuiL.SourceGenerator.Parser.SQuiLShapeCollisionValidator.Finding finding)
	{
		var self = (finding.IsTable ? "@Returns_" : "@Return_") + finding.Name;
		var other = "@Return" + (finding.IsTable ? "s_" : "_") + finding.OtherName;
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0030", "Duplicate Result Shape",
			$"{filename}: `{self}` (line {finding.Line}) has the same result signature as `{other}` (line {finding.OtherLine}) — " +
			"identical column names, order, and C# types (length/precision does not differentiate). " +
			"Result sets can't be routed apart at runtime. Differentiate a column name, order, or C# type; " +
			"or if they are the same shape and meaning, give them the same name to share one record."));
	}

	/// <summary>
	/// SP0032 — timestamp/rowversion is server-generated and read-only; it cannot be a
	/// meaningful input parameter. Declared on an input (@Param_/@Params_ scalar or input-table
	/// column). Use it only on outputs (@Return_/@Returns_), or drop it from the input.
	/// </summary>
	public static void ReportTimestampInput(
		this SourceProductionContext context, string filename,
		SQuiL.SourceGenerator.Parser.SQuiLTimestampInputValidator.Finding finding)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0032", "Timestamp Input Not Allowed",
			$"{filename}: `{finding.Name}` (line {finding.Line}) is a timestamp/rowversion used as an input. " +
			"timestamp is server-generated and read-only — use it only on @Return_/@Returns_ outputs, or remove it."));
	}

	/// <summary>
	/// SP0037 — a standalone <c>null</c>/<c>not null</c> marker on a scalar Declare is invalid
	/// T-SQL. Use an <c>= null</c> initializer to make the scalar nullable, or remove the marker
	/// for non-nullable (the default).
	/// </summary>
	public static void ReportScalarNullabilityMarker(this SourceProductionContext context, string filename,
		SQuiL.SourceGenerator.Parser.SQuiLScalarMarkerValidator.Finding finding)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0037",
			"Scalar Nullability Marker Not Allowed",
			$"{filename}: `{finding.Name}` (line {finding.Line}) has a `null`/`not null` marker, which is invalid T-SQL on a scalar Declare. Use `= null` to make it nullable, or remove the marker for non-nullable."));
	}

	/// <summary>
	/// SP0023 — A <c>[SQuiLQuery]</c> (or a <c>[SQuiLQueryTransaction]</c> with <c>enabled:false</c>)
	/// wraps a body that contains a persistent real-table mutation (UPDATE/INSERT/DELETE/MERGE/EXEC/…).
	/// Consider switching to <c>[SQuiLQueryTransaction]</c> so the mutation is wrapped in a transaction.
	/// </summary>
	public static void ReportMutationNeedsTransaction(
		this SourceProductionContext context, string filename, string mutationKind)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0023",
			"Mutation Under Non-Transactional Query",
			$"{filename}: the query body contains a persistent real-table mutation ({mutationKind}). " +
			"Use [SQuiLQueryTransaction] to wrap the mutation in a transaction."));
	}

	/// <summary>
	/// SP0024 — A <c>[SQuiLQueryTransaction]</c> with <c>enabled:true</c> wraps a body that is
	/// provably read-only (no UPDATE/INSERT/DELETE/MERGE/EXEC/SELECT INTO on a real table).
	/// Consider switching to <c>[SQuiLQuery]</c> to avoid the transaction overhead.
	/// </summary>
	public static void ReportTransactionOnReadOnly(
		this SourceProductionContext context, string filename)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Warning, "SP0024",
			"Transaction On Read-Only Query",
			$"{filename}: no persistent mutation was detected in the query body. " +
			"Use [SQuiLQuery] instead — a transaction wrapper adds overhead with no benefit on a read-only query."));
	}

	/// <summary>
	/// SP0025 — A <c>[SQuiLQueryTransaction]</c> with <c>enabled:true</c> wraps a body that
	/// contains its own <c>Begin Tran</c>/<c>Begin Transaction</c>.  The generated C# wrapper
	/// and the author-supplied SQL transaction will conflict.  Remove the SQL-level transaction,
	/// or set <c>enabled:false</c> to let the author manage the transaction manually.
	/// </summary>
	public static void ReportOwnBeginTran(
		this SourceProductionContext context, string filename)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0025",
			"Own Transaction Inside Generated Transaction",
			$"{filename}: the query body contains a Begin Tran/Begin Transaction statement, " +
			"but [SQuiLQueryTransaction] already wraps the query in a C# DbTransaction. " +
			"Remove the SQL-level transaction, or set enabled:false on [SQuiLQueryTransaction] to manage the transaction manually."));
	}

	/// <summary>
	/// SP0033 — Within one query file's nested-object key graph, a child table/object's column
	/// matches the declared Primary Key of more than one other table/object. A nested-object
	/// child must resolve to exactly one parent, so an ambiguous match is a build error and
	/// the file's code emission is skipped.
	/// </summary>
	public static void ReportAmbiguousKeyLink(
		this SourceProductionContext context, string filename,
		SQuiL.Models.SQuiLKeyFinding finding)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0033", "Ambiguous Key Link",
			$"{filename}: `{finding.Name}` (line {finding.Line}) links to more than one table — it also matches " +
			$"`{finding.OtherName}`'s (line {finding.OtherLine}) primary key. A nested-object child must have " +
			"exactly one parent — rename one of the key columns so only one match remains."));
	}

	/// <summary>
	/// SP0034 — Within one query file's nested-object key graph, following Primary-Key/Foreign-Key
	/// links from a table eventually returns to that same table, forming a cycle. Nested objects
	/// require a tree (no cycles); the file's code emission is skipped.
	/// </summary>
	public static void ReportKeyCycle(
		this SourceProductionContext context, string filename,
		SQuiL.Models.SQuiLKeyFinding finding)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0034", "Key Cycle",
			$"{filename}: `{finding.Name}` (line {finding.Line}) and `{finding.OtherName}` (line {finding.OtherLine}) " +
			"form a primary-key/foreign-key cycle. Nested objects cannot be recursive — remove one of the links."));
	}

	/// <summary>
	/// SP0036 — Within one query file's nested-INPUT key graph, a parent/child link column's
	/// declared type is neither integer-family (int/bigint/smallint) nor uniqueidentifier, so the
	/// generator cannot synthesize a join key for it. The file's code emission is skipped.
	/// </summary>
	public static void ReportUnsupportedKeyType(
		this SourceProductionContext context, string filename,
		string childName, string keyColumn, string sqlType, int line)
	{
		context.ReportDiagnostic(CreateDiagnostic(DiagnosticSeverity.Error, "SP0036", "Unsupported Nested-Input Key Type",
			$"{filename}: the nested-input link column `{keyColumn}` on `{childName}` (line {line}) has type " +
			$"`{sqlType}`, which cannot have a join key synthesized. A nested-input key column must be an integer " +
			"type (int, bigint, or smallint) or uniqueidentifier — change the link column's type."));
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
