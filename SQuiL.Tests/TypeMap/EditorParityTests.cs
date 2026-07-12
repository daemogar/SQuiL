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

        foreach (Match m in EntryRegex.Matches(text))
        {
            var sql = m.Groups["sql"].Value;
            var cs = m.Groups["cs"].Value;
            Assert.True(Expected.ContainsKey(sql), $"editor map has '{sql}' not in the canonical matrix");
            Assert.Equal(Expected[sql], cs);
        }
    }
}
