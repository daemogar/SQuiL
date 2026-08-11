namespace SQuiL.SourceGenerator.Parser;

using SQuiL.Dialects;

using System.Collections.Generic;

/// <summary>
/// SP0041: within one SQuiL file, a <c>Select</c> whose top-level column list is 2+ output-scalar
/// references cannot be routed to a response. The runtime shape key for
/// <c>Select @Return_A, @Return_B</c> is <c>"a:int|b:int"</c>, while the generated switch cases are
/// per-scalar single-column keys — nothing matches, and the switch has no <c>default:</c> arm, so
/// the result set is silently skipped and the caller gets a misleading
/// "Expected return scaler" error. Aliasing does not help.
///
/// <para>
/// Always an Error, on every dialect, and unrelated to the implicit-alias rewrite: the rewrite
/// deliberately leaves multi-scalar selects alone (there is no single name to supply).
/// </para>
///
/// <para>
/// Same rule, different parse substrate, as <c>lintMultiScalarSelect()</c> in <c>parser.ts</c>
/// (VS Code) and <c>LintMultiScalarSelect()</c> in <c>SQuiLLinter.cs</c> (SSMS + Visual Studio) —
/// change one, change all.
/// </para>
/// </summary>
public static class SQuiLMultiScalarSelectValidator
{
    /// <summary>One offending select.</summary>
    /// <param name="Line">1-based line of the <c>Select</c> keyword.</param>
    /// <param name="Names">The declared output-scalar base names referenced, in source order.</param>
    public sealed record Finding(int Line, List<string> Names);

    /// <summary>
    /// Every multi-scalar select in the file. Scans the WHOLE <paramref name="sql"/> (not just the
    /// body block) so offsets are absolute and line numbers are directly computable; a header
    /// <c>Declare</c> or sample <c>Insert … Values</c> never contains a bare output-scalar column
    /// list, so nothing false-fires.
    /// </summary>
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
        if (scalars.Count < 2)
            return findings;

        foreach (var multi in ScalarSelectAliaser.FindMultiScalarSelects(sql, scalars))
            findings.Add(new Finding(LineOf(sql, multi.SelectOffset), multi.DeclaredNames));

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
