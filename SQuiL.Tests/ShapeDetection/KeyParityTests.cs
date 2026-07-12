namespace SQuiL.Tests.ShapeDetection;

using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;
using Xunit;

/// <summary>
/// Guards parity between the build-time shape key (SQuiLShapeKey / Token.CSharpType) and the
/// runtime shape key (SQuiLBaseDataContext.NormalizeType). They live in separate switch tables;
/// a future desync would silently break result-set routing at runtime.
/// </summary>
public class KeyParityTests
{
    /// <summary>
    /// Parse a single-column table declare, extract the build-time canonical token via ShapeKeyOf,
    /// compare against NormalizeType for the matching SQL Server provider type name.
    /// </summary>
    private static void AssertParity(string sqlType, string providerTypeName)
    {
        var tokens = SQuiLTokenizer.GetTokens($"Declare @Returns_T table(C {sqlType});\nUse [Db];\nSelect 1;");
        var blocks = SQuiLParser.ParseTokens(tokens);
        var block = blocks.Find(b => b.IsTable || b.IsObject);
        Assert.NotNull(block);

        var shapeKey = SQuiLShapeKey.ShapeKeyOf(block);
        // Key is "c:<token>" — take the part after the colon.
        var colonIdx = shapeKey.IndexOf(':');
        Assert.True(colonIdx >= 0, $"ShapeKeyOf returned unexpected format: '{shapeKey}'");
        var buildToken = shapeKey.Substring(colonIdx + 1);

        var runtimeToken = SQuiLBaseDataContext.NormalizeTypeForTest(providerTypeName);

        Assert.Equal(buildToken, runtimeToken);
    }

    [Fact] public void Parity_Bit() => AssertParity("bit", "bit");
    [Fact] public void Parity_Int() => AssertParity("int", "int");
    [Fact] public void Parity_Decimal() => AssertParity("decimal(18,2)", "decimal");
    [Fact] public void Parity_Varchar() => AssertParity("varchar(100)", "varchar");
    [Fact] public void Parity_Nvarchar() => AssertParity("nvarchar(50)", "nvarchar");
    [Fact] public void Parity_Date() => AssertParity("date", "date");
    [Fact] public void Parity_Time() => AssertParity("time", "time");
    [Fact] public void Parity_Datetime() => AssertParity("datetime", "datetime");
    [Fact] public void Parity_Datetime2() => AssertParity("datetime2", "datetime2");
    [Fact] public void Parity_Datetimeoffset() => AssertParity("datetimeoffset", "datetimeoffset");
    [Fact] public void Parity_Uniqueidentifier() => AssertParity("uniqueidentifier", "uniqueidentifier");
    [Fact] public void Parity_Varbinary() => AssertParity("varbinary(max)", "varbinary");
    [Fact] public void Parity_Float() => AssertParity("float", "float");
    [Fact] public void Parity_Real() => AssertParity("real", "real");
    [Fact] public void Parity_Bigint() => AssertParity("bigint", "bigint");
    [Fact] public void Parity_Smallint() => AssertParity("smallint", "smallint");
    [Fact] public void Parity_Tinyint() => AssertParity("tinyint", "tinyint");
    [Fact] public void Parity_Money() => AssertParity("money", "money");
    [Fact] public void Parity_Smallmoney() => AssertParity("smallmoney", "smallmoney");
    [Fact] public void Parity_Smalldatetime() => AssertParity("smalldatetime", "smalldatetime");
    [Fact] public void Parity_Xml() => AssertParity("xml", "xml");
    [Fact] public void Parity_Image() => AssertParity("image", "image");
    [Fact] public void Parity_Timestamp() => AssertParity("timestamp", "timestamp");
}
