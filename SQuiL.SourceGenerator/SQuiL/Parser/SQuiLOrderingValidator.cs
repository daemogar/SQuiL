namespace SQuiL.SourceGenerator.Parser;

using System.Collections.Generic;

/// <summary>
/// SP0040: within one SQuiL file, every INPUT declaration (<c>@Param</c>/<c>@Params</c>) must be
/// declared BEFORE any OUTPUT declaration (<c>@Return</c>/<c>@Returns</c>). When an output is
/// declared ahead of some later input the declaration order is reported once, anchored at the
/// FIRST offending output — the earliest output that still has an input declared after it.
///
/// <para>
/// The rule matters most for SQLite, whose native <c>Create Temp Table</c> header must create the
/// input tables before the shred reads them; SQL Server tolerates the interleave. The severity is
/// therefore chosen by the CALLER from the resolved dialect (Error for SQLite, Warning otherwise),
/// exactly like SP0016 — this validator only detects the ordering violation.
/// </para>
///
/// <para>
/// Same rule, different parse substrate, as <c>lintParamsBeforeReturns()</c> in <c>parser.ts</c>
/// (VS Code) and <c>LintParamsBeforeReturns()</c> in <c>SQuiLLinter.cs</c> (SSMS + Visual Studio) —
/// change one, change all.
/// </para>
/// </summary>
public static class SQuiLOrderingValidator
{
    /// <summary>The single ordering violation for a file, anchored at the first offending output.</summary>
    /// <param name="Line">1-based line of the first output declared before a later input.</param>
    public sealed record Finding(int Line);

    /// <summary>Returns the ordering <see cref="Finding"/> for the file, or <c>null</c> when inputs
    /// and outputs are already in the required order (or the file has no inputs).</summary>
    public static Finding? Detect(IEnumerable<CodeBlock> blocks, string sql)
    {
        // Only INPUT/OUTPUT declaration blocks participate, in file (parse) order. Specials
        // (@Debug/@EnvironmentName/…), USE, and BODY blocks are neither and are skipped.
        var decls = new List<CodeBlock>();
        foreach (var block in blocks)
        {
            var isInput = (block.CodeType & CodeType.INPUT) == CodeType.INPUT;
            var isOutput = (block.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT;
            if (isInput || isOutput)
                decls.Add(block);
        }

        // Index of the LAST input; any output before it is out of order. No inputs → nothing
        // can be declared "before" an input, so the rule cannot be violated.
        var lastInputIndex = -1;
        for (var i = 0; i < decls.Count; i++)
            if ((decls[i].CodeType & CodeType.INPUT) == CodeType.INPUT)
                lastInputIndex = i;

        if (lastInputIndex < 0)
            return null;

        // First output that precedes that last input = the first offending output.
        for (var i = 0; i < lastInputIndex; i++)
            if ((decls[i].CodeType & CodeType.OUTPUT) == CodeType.OUTPUT)
                return new Finding(LineOf(sql, decls[i].DatabaseType.Offset));

        return null;
    }

    private static int LineOf(string sql, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < sql.Length; i++)
            if (sql[i] == '\n') line++;
        return line;
    }
}
