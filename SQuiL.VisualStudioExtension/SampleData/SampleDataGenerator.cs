using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SQuiL.VisualStudioExtension.Parsing;

namespace SQuiL.VisualStudioExtension.SampleData;

/// <summary>
/// Generates SQL <c>Insert Into @var (…) Values …;</c> blocks for SQuiL
/// table-valued variables, and locates existing blocks in-place for the
/// "modify" flow.  Port of
/// <c>SQuiL.VSCodeExtension/src/squil/sampleDataGenerator.ts</c>.
///
/// Sample data is for local testing only — the comment heading the inserted
/// block warns the writer to strip it before committing.
/// </summary>
internal static class SampleDataGenerator
{
    /// <summary>
    /// Produce an <c>Insert Into … Values …;</c> block with
    /// <paramref name="count"/> rows for <paramref name="variable"/>.
    /// Returns an empty string if the variable has no columns (caller should
    /// guard before calling).
    /// </summary>
    public static string Generate(SQuiLVariable variable, int count)
    {
        if (variable.Columns is null || variable.Columns.Count == 0)
            return "";

        var cols = variable.Columns;
        string colNames = string.Join(", ", cols.ConvertAll(c => c.Name));

        var rows = new List<string>(count);
        for (int i = 1; i <= count; i++)
        {
            var values = new List<string>(cols.Count);
            foreach (var c in cols)
                values.Add(SampleValue(c, i));
            rows.Add($"    ({string.Join(", ", values)})");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Insert Into {variable.RawName} ({colNames})");
        sb.AppendLine("Values");
        sb.Append(string.Join(",\r\n", rows));
        sb.Append(";");
        return sb.ToString();
    }

    /// <summary>Matches the start of an <c>Insert Into @rawName</c> statement, case-insensitive.</summary>
    private static Regex InsertIntoRegex(string rawName)
    {
        string escaped = Regex.Escape(rawName);
        return new Regex($@"^\s*insert\s+into\s+{escaped}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    /// <summary>
    /// True if an <c>Insert Into @rawName …</c> statement exists anywhere in
    /// <paramref name="text"/>.
    /// </summary>
    public static bool Exists(string text, string rawName)
    {
        var re = InsertIntoRegex(rawName);
        foreach (string line in text.Split('\n'))
            if (re.IsMatch(line)) return true;
        return false;
    }

    /// <summary>
    /// Find the inclusive line range of an existing sample-data block for
    /// <paramref name="rawName"/>.  The block starts at the
    /// <c>Insert Into @rawName …</c> line and ends at the first subsequent
    /// line whose trimmed content ends with <c>;</c>.  Returns
    /// <c>(start, end)</c> or null if not found.
    /// </summary>
    public static (int StartLine, int EndLine)? FindLines(string[] lines, string rawName)
    {
        var re = InsertIntoRegex(rawName);
        for (int i = 0; i < lines.Length; i++)
        {
            if (!re.IsMatch(lines[i])) continue;
            for (int j = i; j < lines.Length; j++)
            {
                if (lines[j].TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    return (i, j);
            }
            // No terminator found — treat the start line alone as the block.
            return (i, i);
        }
        return null;
    }

    // ── Type-aware sample value generation ────────────────────────────────

    private static string SampleValue(TableColumn col, int index)
    {
        string baseType = col.SqlType.ToLowerInvariant();
        int paren = baseType.IndexOf('(');
        if (paren >= 0) baseType = baseType.Substring(0, paren).Trim();
        baseType = baseType.Trim();

        // CultureInfo.InvariantCulture: keep '.00' regardless of OS locale —
        // SQL Server is happy with '.', not ','.
        switch (baseType)
        {
            case "int":
            case "bigint":
            case "smallint":
            case "tinyint":
                return index.ToString(CultureInfo.InvariantCulture);

            case "bit":
                return "1";

            case "decimal":
            case "numeric":
            case "float":
            case "real":
            case "money":
            case "smallmoney":
                return $"{index}.00";

            case "varchar":
            case "nvarchar":
            case "char":
            case "nchar":
            case "text":
            case "ntext":
                return $"'{col.Name} {index}'";

            case "datetime":
            case "datetime2":
            case "smalldatetime":
                return $"'2024-01-{index:00} 00:00:00'";

            case "date":
                return $"'2024-01-{index:00}'";

            case "time":
                return $"'{index:00}:00:00'";

            case "datetimeoffset":
                return $"'2024-01-{index:00} 00:00:00 +00:00'";

            case "uniqueidentifier":
                return "NewID()";

            case "varbinary":
            case "binary":
            case "image":
                return "0x00";

            case "xml":
                return $"'<root>{index}</root>'";

            default:
                return col.Nullable ? "Null" : $"'{col.Name}_{index}'";
        }
    }
}
