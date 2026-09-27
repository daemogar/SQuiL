namespace SQuiL.SourceGenerator.Parser;

using System;
using System.Collections.Generic;

/// <summary>
/// SP0043: a plural direction prefix (<c>@Params_</c> / <c>@Returns_</c>) declares a LIST, so the
/// declare must carry a <c>table(...)</c> type. A plural prefix on a scalar type is a build error.
///
/// <para>
/// Without this, <see cref="SQuiLParser"/> silently classifies <c>Declare @Returns_Total int;</c>
/// as an ordinary output scalar named <c>Total</c>, while
/// <c>SqlServerDialect.RewriteOutputSelects</c> reconstructs its lookup key as
/// <c>@Return_&lt;Name&gt;</c> — so the plural spelling never receives the implicit
/// <c>As [Total]</c> alias, the returned column stays UNNAMED, the runtime shape key matches no
/// generated case, and the result set is dropped with the response property left at its default.
/// A silent wrong answer, which is why this is an error rather than a widened alias lookup.
/// </para>
///
/// <para>
/// SQL Server only, structurally rather than by a dialect gate: a temp-table-header dialect
/// (SQLite, PostgreSQL) has no <c>@</c>-prefixed declare at all, and
/// <see cref="SQuiLParser"/> collapses a single-column <c>Create Temp Table</c> to a scalar ONLY
/// for the SINGULAR form — so no plural temp-table declaration ever reaches the scalar path, and
/// <see cref="LeadingVariable"/> returns <c>null</c> for its <c>Original</c> text anyway.
/// </para>
///
/// <para>
/// Mirrored as an editor squiggle by <c>lintPluralScalarDeclare</c> in <c>parser.ts</c> (VS Code)
/// and <c>LintPluralScalarDeclare</c> in both <c>SQuiLLinter.cs</c> copies — change one, change all.
/// </para>
/// </summary>
public static class SQuiLPluralScalarValidator
{
	/// <summary>One offending declare.</summary>
	/// <param name="Variable">The variable as written, e.g. <c>@Returns_Total</c>.</param>
	/// <param name="Suggestion">The singular rename, e.g. <c>@Return_Total</c>.</param>
	/// <param name="Line">1-based line of the declare.</param>
	public sealed record Finding(string Variable, string Suggestion, int Line);

	public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
	{
		var findings = new List<Finding>();
		foreach (var block in blocks)
		{
			// Tables and objects are exactly what a plural prefix is SUPPOSED to be.
			if (block.IsTable || block.IsObject) continue;
			// A bare special (@Debug/@SuppressDebug/@EnvironmentName/@AsOfDate) carries no prefix.
			if (block.IsSpecialDeclaration) continue;

			var variable = LeadingVariable(block.DatabaseType.Original);
			if (variable is null) continue;

			if (variable.StartsWith("@Params_", StringComparison.OrdinalIgnoreCase))
				findings.Add(new Finding(
					variable,
					"@Param_" + variable.Substring("@Params_".Length),
					LineOf(sql, block.DatabaseType.Offset)));
			else if (variable.StartsWith("@Returns_", StringComparison.OrdinalIgnoreCase))
				findings.Add(new Finding(
					variable,
					"@Return_" + variable.Substring("@Returns_".Length),
					LineOf(sql, block.DatabaseType.Offset)));
		}
		return findings;
	}

	/// <summary>
	/// The leading <c>@variable</c> token of a scalar block's <c>DatabaseType.Original</c> (which
	/// holds the whole declare text, e.g. <c>"@Returns_Total int"</c>), or <c>null</c> when the
	/// text does not start with <c>@</c> — the temp-table-header case, whose Original is a bare
	/// table name like <c>"Return_V INTEGER"</c>.
	/// </summary>
	private static string? LeadingVariable(string? original)
	{
		if (string.IsNullOrEmpty(original) || original![0] != '@') return null;
		var j = 1;
		while (j < original.Length && (char.IsLetterOrDigit(original[j]) || original[j] == '_')) j++;
		return original.Substring(0, j);
	}

	private static int LineOf(string sql, int offset)
	{
		var line = 1;
		for (var i = 0; i < offset && i < sql.Length; i++)
			if (sql[i] == '\n') line++;
		return line;
	}
}
