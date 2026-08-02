namespace SQuiL.Tests.TypeMap;

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Guards the two C# editor SqlTypeMap.cs copies against drift from the canonical
/// SQL→C# matrix (and from each other). Reads the source files from disk — the editor
/// projects are VSSDK and not referenced by this test project.
/// </summary>
public class EditorParityTests
{
    // Canonical SQL→short-C# expected in the editor maps (no System. prefix, no nullability).
    private static readonly Dictionary<string, string> Expected = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["bit"] = "bool", ["int"] = "int", ["bigint"] = "long", ["smallint"] = "short",
        ["tinyint"] = "byte", ["real"] = "float", ["float"] = "double",
        ["decimal"] = "decimal", ["numeric"] = "decimal", ["money"] = "decimal", ["smallmoney"] = "decimal",
        ["char"] = "string", ["nchar"] = "string", ["varchar"] = "string", ["nvarchar"] = "string",
        ["text"] = "string", ["ntext"] = "string", ["xml"] = "string",
        ["date"] = "DateOnly", ["time"] = "TimeOnly",
        ["datetime"] = "DateTime", ["datetime2"] = "DateTime", ["smalldatetime"] = "DateTime",
        ["datetimeoffset"] = "DateTimeOffset", ["uniqueidentifier"] = "Guid",
        ["binary"] = "byte[]", ["varbinary"] = "byte[]", ["image"] = "byte[]", ["timestamp"] = "byte[]",
    };

    private static readonly Regex EntryRegex =
        new(@"\[""(?<sql>[a-z0-9]+)""\]\s*=\s*""(?<cs>[^""]+)""", RegexOptions.IgnoreCase);

    // Isolates the body of a single `private static readonly Dictionary<...> <name> = new(...) { ... };`
    // declaration so the two dialect maps (SQL Server `Map` vs SQLite `SqliteMap`, added in Task 4 —
    // see squil skill / project_todo_multidb_dialect.md) can be checked against their own canonical
    // matrix instead of colliding in one blanket regex pass over the whole file.
    // Fragile-if-extended: the brace-depth scan below assumes `dictionaryName` is the ONLY thing
    // between the opening `{` it finds and the matching closing `{`, and that no dictionary VALUE
    // contains a literal '{' or '}' character — fine for today's two flat SQL→C# maps, but a third
    // dictionary or a value with braces in it would need a smarter scan (e.g. skip over string
    // literals) rather than a plain character count.
    private static string ExtractDictionaryBody(string text, string dictionaryName)
    {
        var m = Regex.Match(text, $@"\b{dictionaryName}\s*=\s*new[^{{]*\{{", RegexOptions.Singleline);
        Assert.True(m.Success, $"could not locate the '{dictionaryName}' dictionary declaration");

        int start = m.Index + m.Length;
        int depth = 1;
        int i = start;
        for (; i < text.Length && depth > 0; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
        }
        return text.Substring(start, i - start);
    }

    private static string RepoRoot([CallerFilePath] string path = "")
        // this file is SQuiL.Tests/TypeMap/EditorParityTests.cs → up 2 to repo root.
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    [Theory]
    [InlineData("SQuiL.SsmsExtension/Parsing/SqlTypeMap.cs")]
    [InlineData("SQuiL.VisualStudioExtension/Parsing/SqlTypeMap.cs")]
    public void EditorMapAgreesWithMatrix(string relPath)
    {
        var full = Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"missing {full}");
        var text = File.ReadAllText(full);

        // Only the SQL Server dialect's `Map` dictionary is checked against the canonical
        // SQL Server matrix. The SQLite overlay (`SqliteMap`) is guarded separately by
        // SqliteEditorMapAgreesWithMatrix below, against a SQLite-specific matrix.
        var mapBody = ExtractDictionaryBody(text, "Map");

        foreach (Match m in EntryRegex.Matches(mapBody))
        {
            var sql = m.Groups["sql"].Value;
            var cs = m.Groups["cs"].Value;
            Assert.True(Expected.ContainsKey(sql), $"editor map has '{sql}' not in the canonical matrix");
            Assert.Equal(Expected[sql], cs);
        }
    }

    // Canonical SQLite→short-C# matrix (Task 4 / TODO #6 Phase 3B), matching
    // SQLITE_TO_CS in SQuiL.VSCodeExtension/src/squil/previewGenerator.ts.
    private static readonly Dictionary<string, string> ExpectedSqlite = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["integer"] = "long", ["text"] = "string", ["real"] = "double", ["blob"] = "byte[]",
        ["numeric"] = "decimal", ["boolean"] = "bool", ["date"] = "DateTime", ["datetime"] = "DateTime",
        ["guid"] = "Guid", ["uniqueidentifier"] = "Guid",
    };

    [Theory]
    [InlineData("SQuiL.SsmsExtension/Parsing/SqlTypeMap.cs")]
    [InlineData("SQuiL.VisualStudioExtension/Parsing/SqlTypeMap.cs")]
    public void SqliteEditorMapAgreesWithMatrix(string relPath)
    {
        var full = Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"missing {full}");
        var text = File.ReadAllText(full);

        var sqliteMapBody = ExtractDictionaryBody(text, "SqliteMap");

        foreach (Match m in EntryRegex.Matches(sqliteMapBody))
        {
            var sql = m.Groups["sql"].Value;
            var cs = m.Groups["cs"].Value;
            Assert.True(ExpectedSqlite.ContainsKey(sql), $"editor SqliteMap has '{sql}' not in the canonical SQLite matrix");
            Assert.Equal(ExpectedSqlite[sql], cs);
        }
    }

    // Canonical PostgreSQL->short-C# matrix (Task 7 fix round 1 — the PG overlay was missing
    // entirely; every PG-only spelling fell through to `object`), matching POSTGRES_TO_CS in
    // SQuiL.VSCodeExtension/src/squil/previewGenerator.ts. Regex keys are lowercase a-z0-9 only
    // (see EntryRegex), so multi-word ANSI long-form spellings (`character varying`, `timestamp
    // without time zone`, etc.) never match this regex and are intentionally NOT asserted here —
    // they're covered instead by the exact-string unit tests in previewGenerator.test.ts.
    private static readonly Dictionary<string, string> ExpectedPostgres = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["int2"] = "short", ["smallint"] = "short",
        ["int4"] = "int", ["int"] = "int", ["integer"] = "int",
        ["int8"] = "long", ["bigint"] = "long",
        ["text"] = "string", ["varchar"] = "string", ["char"] = "string", ["bpchar"] = "string",
        ["json"] = "string", ["jsonb"] = "string",
        ["bytea"] = "byte[]",
        ["uuid"] = "Guid",
        ["bool"] = "bool", ["boolean"] = "bool",
        ["timestamp"] = "DateTime",
        ["timestamptz"] = "DateTimeOffset",
        ["date"] = "DateOnly",
        ["time"] = "TimeOnly",
        ["numeric"] = "decimal", ["decimal"] = "decimal", ["money"] = "decimal",
        ["real"] = "float", ["float4"] = "float",
        ["float8"] = "double",
    };

    [Theory]
    [InlineData("SQuiL.SsmsExtension/Parsing/SqlTypeMap.cs")]
    [InlineData("SQuiL.VisualStudioExtension/Parsing/SqlTypeMap.cs")]
    public void PostgresEditorMapAgreesWithMatrix(string relPath)
    {
        var full = Path.Combine(RepoRoot(), relPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"missing {full}");
        var text = File.ReadAllText(full);

        var postgresMapBody = ExtractDictionaryBody(text, "PostgresMap");

        foreach (Match m in EntryRegex.Matches(postgresMapBody))
        {
            var sql = m.Groups["sql"].Value;
            var cs = m.Groups["cs"].Value;
            Assert.True(ExpectedPostgres.ContainsKey(sql), $"editor PostgresMap has '{sql}' not in the canonical Postgres matrix");
            Assert.Equal(ExpectedPostgres[sql], cs);
        }
    }
}
