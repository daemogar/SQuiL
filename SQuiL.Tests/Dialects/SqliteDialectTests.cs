using SQuiL.Dialects;
using Xunit;

namespace SQuiL.Tests.Dialects;

public class SqliteDialectTests
{
    private readonly ISqlDialect _dialect = new SqliteDialect();

    [Fact] public void UsingDirectives_is_sqlite()
        => Assert.Contains("using Microsoft.Data.Sqlite;", _dialect.UsingDirectives());

    [Fact] public void ProviderExceptionType_is_SqliteException()
        => Assert.Equal("SqliteException", _dialect.ProviderExceptionType());

    [Fact] public void RuntimeBaseType_is_SqliteDataContext()
        => Assert.Equal("SqliteDataContext", _dialect.RuntimeBaseType());

    [Fact] public void DatabaseDirective_is_empty()
        => Assert.Equal("", _dialect.DatabaseDirective("AnyDb"));

    [Fact] public void VarCharType_and_BitType_are_sqlite_types()
    {
        Assert.Equal("Microsoft.Data.Sqlite.SqliteType.Text", _dialect.VarCharType());
        Assert.Equal("Microsoft.Data.Sqlite.SqliteType.Integer", _dialect.BitType());
    }

    [Fact]
    public void ShredStatement_uses_json_each()
    {
        var block = SqliteDialectTestHelper.ParseSingleInputBlock(
            "Create Temp Table Params_Person (PersonID INTEGER, Name TEXT);");
        var sql = new SqliteDialect().ShredStatement(block);
        Assert.Contains("From json_each(@__json_Params_Person)", sql);
        Assert.Contains("json_extract(value, '$.PersonID')", sql);
        Assert.Contains("json_extract(value, '$.Name')", sql);
        Assert.StartsWith("Insert Into Params_Person", sql);
    }
}
