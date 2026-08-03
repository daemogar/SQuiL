namespace SQuiL.Tests.ShapeDetection;

using Microsoft.Data.Sqlite;
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
    /// SqliteDataContext.NormalizeType for the provider type name a REAL Microsoft.Data.Sqlite
    /// reader reports.
    /// <para>
    /// The runtime provider type name is NOT hand-fed — it is derived from an actual round trip:
    /// a <c>Create Temp Table</c> is built with a single column of the EXACT decltype the generator
    /// bakes into its own header (<c>block.Properties[0].Type.Original</c> — e.g. <c>float</c> for a
    /// declared <c>double</c>, <c>varbinary(max)</c> for a <c>varbinary</c>), the column is selected,
    /// and <c>DbDataReader.GetDataTypeName(0)</c> is read exactly as <c>SQuiLBaseDataContext.ShapeKey</c>
    /// would at runtime. This guards against a fictional/self-consistent affinity string that a live
    /// reader would never return.
    /// </para>
    /// </summary>
    private static void AssertParitySqlite(string sqlType)
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

        // Faithful runtime observation: create a column with the SAME decltype the generator emits
        // (Token.Original, which TableVariableDeclaration writes verbatim), select it, and read what
        // Microsoft.Data.Sqlite actually reports for GetDataTypeName — the real routing input.
        var emittedDeclType = block!.Properties[0].Type.Original!;
        var providerTypeName = ReadProviderTypeName(emittedDeclType);

        var runtimeToken = new SqliteProbe().NormalizeTypeForTest(providerTypeName);

        Assert.Equal(buildToken, runtimeToken);
    }

    /// <summary>
    /// Creates a temp table with a single column of <paramref name="declType"/>, selects it, and
    /// returns the live <c>DbDataReader.GetDataTypeName(0)</c> — the exact provider type name the
    /// runtime router (<c>SQuiLBaseDataContext.ShapeKey</c>) observes for such a column.
    /// </summary>
    private static string ReadProviderTypeName(string declType)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"Create Temp Table __parity_probe (C {declType});";
            create.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.CommandText = "Select C From __parity_probe;";
        using var reader = select.ExecuteReader();
        Assert.True(reader.Read() || reader.FieldCount == 1);
        return reader.GetDataTypeName(0);
    }

    // Every spelling that tokenizes to one of the eight SQLite-supported token types (the set
    // SqliteDialect.SqliteReader maps to a typed reader accessor): TYPE_BIGINT, TYPE_STRING,
    // TYPE_DOUBLE, TYPE_VARBINARY, TYPE_DECIMAL, TYPE_BOOLEAN, TYPE_DATETIME, TYPE_GUID. Build
    // RoutingType must equal runtime NormalizeType(GetDataTypeName) for each — no fictional inputs.
    [Fact] public void Sqlite_Parity_Integer()          => AssertParitySqlite("INTEGER");
    [Fact] public void Sqlite_Parity_Bigint()           => AssertParitySqlite("bigint");
    [Fact] public void Sqlite_Parity_Text()             => AssertParitySqlite("TEXT");
    [Fact] public void Sqlite_Parity_Varchar()          => AssertParitySqlite("varchar(100)");
    [Fact] public void Sqlite_Parity_Nvarchar()         => AssertParitySqlite("nvarchar(50)");
    [Fact] public void Sqlite_Parity_Real()             => AssertParitySqlite("REAL");
    [Fact] public void Sqlite_Parity_Float()            => AssertParitySqlite("float");
    [Fact] public void Sqlite_Parity_Double()           => AssertParitySqlite("double");
    [Fact] public void Sqlite_Parity_Blob()             => AssertParitySqlite("BLOB");
    // NB: `varbinary` is the byte[] token's OTHER spelling, but its emitted decltype `varbinary(max)`
    // is invalid SQLite ("max" is not a numeric type-arg) so no live column of it can be created —
    // BLOB is SQLite's binary spelling and covers the byte[] routing token. NormalizeType still maps
    // "varbinary" defensively (see SqliteDataContext) in case that generator gap is ever closed.
    [Fact] public void Sqlite_Parity_Numeric()          => AssertParitySqlite("NUMERIC");
    [Fact] public void Sqlite_Parity_Decimal()          => AssertParitySqlite("decimal(18,2)");
    [Fact] public void Sqlite_Parity_Boolean()          => AssertParitySqlite("BOOLEAN");
    [Fact] public void Sqlite_Parity_Bit()              => AssertParitySqlite("bit");
    [Fact] public void Sqlite_Parity_Date()             => AssertParitySqlite("DATE");
    [Fact] public void Sqlite_Parity_Datetime()         => AssertParitySqlite("DATETIME");
    [Fact] public void Sqlite_Parity_Datetime2()        => AssertParitySqlite("datetime2");
    [Fact] public void Sqlite_Parity_Guid()             => AssertParitySqlite("GUID");
    [Fact] public void Sqlite_Parity_Uniqueidentifier() => AssertParitySqlite("uniqueidentifier");

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
