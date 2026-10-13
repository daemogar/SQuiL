namespace SQuiL.Tests.TypeMap;

using SQuiL.Tokenizer;
using Xunit;

/// <summary>
/// Canonical SQL→C# type matrix. Asserts Token's three emit methods directly so the
/// generator side of the type map can never silently drift. One row per SQL type;
/// see the plan's Global Constraints matrix for the source of truth.
/// </summary>
public class TypeMapTests
{
    [Theory]
    // type,                     csharp,                    reader,                                        sqlDbType
    [InlineData(TokenType.TYPE_BOOLEAN,        "bool",                    "reader.GetBoolean",                           "System.Data.SqlDbType.Bit")]
    [InlineData(TokenType.TYPE_INT,            "int",                     "reader.GetInt32",                             "System.Data.SqlDbType.Int")]
    [InlineData(TokenType.TYPE_DECIMAL,        "decimal",                 "reader.GetDecimal",                           "System.Data.SqlDbType.Decimal")]
    [InlineData(TokenType.TYPE_DATE,           "System.DateOnly",         "reader.GetFieldValue<System.DateOnly>",       "System.Data.SqlDbType.Date")]
    [InlineData(TokenType.TYPE_TIME,           "System.TimeOnly",         "reader.GetFieldValue<System.TimeOnly>",       "System.Data.SqlDbType.Time")]
    [InlineData(TokenType.TYPE_DATETIME,       "System.DateTime",         "reader.GetDateTime",                          "System.Data.SqlDbType.DateTime")]
    [InlineData(TokenType.TYPE_DATETIMEOFFSET, "System.DateTimeOffset",   "reader.GetFieldValue<System.DateTimeOffset>", "System.Data.SqlDbType.DateTimeOffset")]
    [InlineData(TokenType.TYPE_GUID,           "System.Guid",             "reader.GetGuid",                              "System.Data.SqlDbType.UniqueIdentifier")]
    [InlineData(TokenType.TYPE_BINARY,         "byte[]",                  "reader.GetFieldValue<byte[]>",                "System.Data.SqlDbType.Binary")]
    [InlineData(TokenType.TYPE_VARBINARY,      "byte[]",                  "reader.GetFieldValue<byte[]>",                "System.Data.SqlDbType.VarBinary")]
    [InlineData(TokenType.TYPE_FLOAT,          "float",                   "reader.GetFloat",                             "System.Data.SqlDbType.Real")]
    [InlineData(TokenType.TYPE_DOUBLE,         "double",                  "reader.GetDouble",                            "System.Data.SqlDbType.Float")]
    [InlineData(TokenType.TYPE_BIGINT,   "long",  "reader.GetInt64", "System.Data.SqlDbType.BigInt")]
    [InlineData(TokenType.TYPE_SMALLINT, "short", "reader.GetInt16", "System.Data.SqlDbType.SmallInt")]
    [InlineData(TokenType.TYPE_TINYINT,  "byte",  "reader.GetByte",  "System.Data.SqlDbType.TinyInt")]
    [InlineData(TokenType.TYPE_MONEY,      "decimal", "reader.GetDecimal", "System.Data.SqlDbType.Money")]
    [InlineData(TokenType.TYPE_SMALLMONEY, "decimal", "reader.GetDecimal", "System.Data.SqlDbType.SmallMoney")]
    [InlineData(TokenType.TYPE_SMALLDATETIME, "System.DateTime", "reader.GetDateTime", "System.Data.SqlDbType.SmallDateTime")]
    [InlineData(TokenType.TYPE_XML,           "string",          "reader.GetString",  "System.Data.SqlDbType.Xml")]
    [InlineData(TokenType.TYPE_IMAGE,         "byte[]",          "reader.GetFieldValue<byte[]>", "System.Data.SqlDbType.Image")]
    [InlineData(TokenType.TYPE_TIMESTAMP, "byte[]", "reader.GetFieldValue<byte[]>", "System.Data.SqlDbType.Timestamp")]
    public void TypeMapping(TokenType type, string csharp, string reader, string sqlDbType)
    {
        var token = new Token(type, 0, "");
        Assert.Equal(csharp,    token.CSharpType());
        Assert.Equal(reader,    token.DataReader());
        // VarBinary size arg not exercised here; scalar types only.
        Assert.Equal(sqlDbType, token.SqlDbType(allowNullSize: true));
    }

    /// <summary>
    /// Every string spelling collapses to <c>TYPE_STRING</c>, so the parameter type comes from
    /// the declared spelling. An <c>n</c>-type sent as <c>VarChar</c> loses non-Latin text (TODO #31).
    /// </summary>
    [Theory]
    // declared,  size,    sqlDbType
    [InlineData("char",     "5",     "System.Data.SqlDbType.Char, 5")]
    [InlineData("nchar",    "5",     "System.Data.SqlDbType.NChar, 5")]
    [InlineData("varchar",  "10",    "System.Data.SqlDbType.VarChar, 10")]
    [InlineData("nvarchar", "10",    "System.Data.SqlDbType.NVarChar, 10")]
    [InlineData("nvarchar", "max",   "System.Data.SqlDbType.NVarChar, -1")]
    [InlineData("varchar",  "max",   "System.Data.SqlDbType.VarChar, -1")]
    [InlineData("text",     "text",  "System.Data.SqlDbType.Text")]
    [InlineData("ntext",    "ntext", "System.Data.SqlDbType.NText")]
    [InlineData(null,       "10",    "System.Data.SqlDbType.VarChar, 10")]
    public void StringTypeMapping(string? declared, string size, string sqlDbType)
    {
        var token = new Token(TokenType.TYPE_STRING, 0, size) { SqlTypeName = declared };
        Assert.Equal(sqlDbType, token.SqlDbType(size));
        // SQL char(n) is a string, never System.Char.
        Assert.Equal("string", token.CSharpType());
    }
}
