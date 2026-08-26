namespace SQuiL.SourceGenerator.Parser;

using SQuiL.Dialects;

using System.Collections.Generic;

/// <summary>
/// SP0044: a bare output-scalar <c>Select</c> followed by <c>throw</c> or <c>go</c> — the only
/// members of <c>ScalarSelectAliaser.StatementStarters</c> that are NOT T-SQL reserved words, and
/// therefore the only ones that are equally valid as an AS-less column alias.
///
/// <para>
/// <c>Select @Return_Count Throw;</c> is valid T-SQL: T-SQL reads <c>Throw</c> as the column's
/// alias. The scanner reads it as the next statement, concludes the select is bare, and appends
/// <c>As [Count]</c> — emitting <c>Select @Return_Count As [Count] Throw;</c>, which does not
/// parse. Rather than guess, the rewrite declines (see
/// <c>ScalarSelectAliaser.FindAmbiguousScalarSelects</c>) and this validator asks the author to
/// disambiguate. Declining alone is not enough: an un-aliased bare select silently loses its
/// result set, which is exactly the failure SP0043 exists to prevent.
/// </para>
///
/// <para>
/// Scans the WHOLE <paramref name="sql"/>, like <see cref="SQuiLMultiScalarSelectValidator"/> —
/// a header <c>Declare</c> never contains a bare output-scalar column list, so nothing
/// false-fires. SQL Server only: the temp-table dialects no-op their rewrite entirely.
/// </para>
///
/// <para>
/// Mirrored as an editor squiggle by <c>lintAmbiguousScalarAlias</c> in <c>parser.ts</c> and
/// <c>LintAmbiguousScalarAlias</c> in both <c>SQuiLLinter.cs</c> copies — change one, change all.
/// </para>
/// </summary>
public static class SQuiLAmbiguousAliasValidator
{
	/// <summary>One ambiguous select.</summary>
	/// <param name="Line">1-based line of the scalar reference.</param>
	/// <param name="Name">The declared output-scalar base name.</param>
	/// <param name="Terminator">The ambiguous word as written, e.g. <c>Throw</c>.</param>
	public sealed record Finding(int Line, string Name, string Terminator);

	public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
	{
		var scalars = new Dictionary<string, string>();
		foreach (var block in blocks)
		{
			if (block.CodeType != CodeType.OUTPUT_VARIABLE)
				continue;
			scalars[$"@Return_{block.Name}".ToLowerInvariant()] = block.Name;
		}

		var findings = new List<Finding>();
		if (scalars.Count == 0)
			return findings;

		foreach (var bare in ScalarSelectAliaser.FindAmbiguousScalarSelects(sql, scalars))
			findings.Add(new Finding(LineOf(sql, bare.VariableOffset), bare.DeclaredName, bare.Terminator));

		return findings;
	}

	private static int LineOf(string sql, int offset)
	{
		var line = 1;
		for (var i = 0; i < offset && i < sql.Length; i++)
			if (sql[i] == '\n') line++;
		return line;
	}
}
