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
    public void TypeMapping(TokenType type, string csharp, string reader, string sqlDbType)
    {
        var token = new Token(type, 0, "");
        Assert.Equal(csharp,    token.CSharpType());
        Assert.Equal(reader,    token.DataReader());
        // VarBinary size arg not exercised here; scalar types only.
        Assert.Equal(sqlDbType, token.SqlDbType(allowNullSize: true));
    }
}
