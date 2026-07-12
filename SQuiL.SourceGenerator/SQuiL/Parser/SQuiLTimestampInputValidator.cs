namespace SQuiL.SourceGenerator.Parser;

using SQuiL.Tokenizer;

using System.Collections.Generic;

/// <summary>
/// SP0032: timestamp/rowversion is a server-generated, read-only value and cannot be a
/// meaningful input. Flags any INPUT declaration (scalar @Param_/@Params_ or a column of an
/// input table) whose SQL type is timestamp/rowversion. Output declarations are fine (byte[]).
/// </summary>
public static class SQuiLTimestampInputValidator
{
    public sealed record Finding(string Name, int Line);

    public static List<Finding> Detect(IEnumerable<CodeBlock> blocks, string sql)
    {
        var findings = new List<Finding>();
        foreach (var block in blocks)
        {
            if ((block.CodeType & CodeType.INPUT) != CodeType.INPUT) continue;

            // Scalar input of timestamp type.
            if (block.DatabaseType.Type == TokenType.TYPE_TIMESTAMP)
                findings.Add(new Finding(block.Name, LineOf(sql, block.DatabaseType.Offset)));

            // Input table/object columns of timestamp type.
            // CodeItem: `Type` is the SQL type token, `Identifier` is the name token.
            if (block.Properties is { } props)
                foreach (var col in props)
                    if (col.Type.Type == TokenType.TYPE_TIMESTAMP)
                        findings.Add(new Finding($"{block.Name}.{col.Identifier.Value}",
                            LineOf(sql, col.Type.Offset)));
        }
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
