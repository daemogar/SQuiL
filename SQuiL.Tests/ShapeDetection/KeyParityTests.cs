namespace SQuiL.Tests.ShapeDetection;

using Microsoft.Extensions.Configuration;

using SQuiL.Dialects;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

/// <summary>
/// Guards parity between the build-time shape key (SQuiLShapeKey / Token.CSharpType) and the
/// runtime shape key (each provider's <c>NormalizeType</c> override). They live in separate
/// switch tables; a future desync would silently break result-set routing at runtime.
/// </summary>
public class KeyParityTests
{
    // Minimal concrete contexts so the protected, virtually-dispatched NormalizeType override
    // for each provider can be reached from a test (mirrors SqliteCreateErrorTests.TestContext).
    private sealed class SqlServerProbe() : SqlServerDataContext(new ConfigurationBuilder().Build());
    private sealed class SqliteProbe() : SqliteDataContext(new ConfigurationBuilder().Build());

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

        var runtimeToken = new SqlServerProbe().NormalizeTypeForTest(providerTypeName);

        Assert.Equal(buildToken, runtimeToken);
    }

    /// <summary>
    /// SQLite counterpart of <see cref="AssertParity"/>: tokenizes <paramref name="sqlType"/> under
    /// <see cref="SqliteDialect"/> (so SQLite-only keywords like INTEGER/BLOB/BOOLEAN/GUID resolve),
    /// via the real Create-Temp-Table header (Task 5), extracts the build-time canonical routing
    /// token via the dialect-aware ShapeKeyOf overload, and compares against
    /// SqliteDataContext.NormalizeType for the matching provider type name.
    /// </summary>
    private static void AssertParitySqlite(string sqlType, string providerTypeName)
    {
        var dialect = new SqliteDialect();
        var tokens = SQuiLTokenizer.GetTokens($"Create Temp Table Returns_T (C {sqlType});\nSelect 1;", dialect);
        var blocks = SQuiLParser.ParseTokens(tokens, dialect);
        var block = blocks.Find(b => b.IsTable || b.IsObject);
        Assert.NotNull(block);

        var shapeKey = SQuiLShapeKey.ShapeKeyOf(block, dialect);
        var colonIdx = shapeKey.IndexOf(':');
        Assert.True(colonIdx >= 0, $"ShapeKeyOf returned unexpected format: '{shapeKey}'");
        var buildToken = shapeKey.Substring(colonIdx + 1);

        var runtimeToken = new SqliteProbe().NormalizeTypeForTest(providerTypeName);

        Assert.Equal(buildToken, runtimeToken);
    }

    [Fact] public void Sqlite_Parity_Integer() => AssertParitySqlite("INTEGER", "INTEGER");
    [Fact] public void Sqlite_Parity_Text()    => AssertParitySqlite("TEXT", "TEXT");
    [Fact] public void Sqlite_Parity_Real()    => AssertParitySqlite("REAL", "REAL");
    [Fact] public void Sqlite_Parity_Blob()    => AssertParitySqlite("BLOB", "BLOB");
    [Fact] public void Sqlite_Parity_Numeric() => AssertParitySqlite("NUMERIC", "NUMERIC");
    [Fact] public void Sqlite_Parity_Boolean() => AssertParitySqlite("BOOLEAN", "INTEGER");
    [Fact] public void Sqlite_Parity_Datetime()=> AssertParitySqlite("DATETIME", "TEXT");
    [Fact] public void Sqlite_Parity_Guid()    => AssertParitySqlite("GUID", "TEXT");

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

    /// <summary>
    /// Zero-churn identity guard for the generator's dialect-aware emission (Task 4 follow-up
    /// fix): <c>ShapeKeyOf(block, new SqlServerDialect())</c> MUST be byte-identical to
    /// <c>ShapeKeyOf(block)</c> for SQL Server, since SQuiLDataContext.cs now always calls the
    /// dialect-aware overload for switch-case labels (both the flat and nested emission paths).
    /// If this test ever fails, every SQL-Server `*DataContext.g.verified.cs` snapshot's switch
    /// labels would silently change — see RoutingType in SQuiLShapeKey.cs, which must return
    /// `p.CSharpType()` (identical to the non-dialect overload) for any non-SQLite dialect.
    /// </summary>
    [Fact]
    public void ShapeKeyOf_SqlServerDialect_MatchesPlainOverload_MultiColumn()
    {
        var tokens = SQuiLTokenizer.GetTokens(
            "Declare @Returns_People table(PersonID int, Name varchar(100), IsActive bit, Created datetime, RowGuid uniqueidentifier, Amount decimal(18,2) null);\nUse [Db];\nSelect 1;");
        var blocks = SQuiLParser.ParseTokens(tokens);
        var block = blocks.Find(b => b.IsTable || b.IsObject);
        Assert.NotNull(block);

        var plain = SQuiLShapeKey.ShapeKeyOf(block!);
        var dialectAware = SQuiLShapeKey.ShapeKeyOf(block!, new SqlServerDialect());

        Assert.Equal(plain, dialectAware);
    }
}
