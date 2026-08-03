using Microsoft.Extensions.Configuration;
using SQuiL;
using Xunit;

namespace SQuiL.Tests.Dialects;

/// <summary>
/// Mirrors <see cref="SqliteCreateErrorTests"/>, but for <see cref="PostgresDataContext"/>.
/// <para>
/// NOTE: unlike <c>Microsoft.Data.Sqlite</c>'s <c>SqliteException(message, errorCode,
/// extendedErrorCode)</c>, Npgsql's <c>NpgsqlException</c> exposes no public constructor that lets a
/// test populate <see cref="System.Data.Common.DbException.ErrorCode"/> — that property is inherited
/// from <see cref="System.Runtime.InteropServices.ExternalException"/> and is backed by
/// <c>Exception.HResult</c>, which Npgsql only sets internally when the driver raises the exception
/// against a live connection. Constructing a meaningfully-populated <c>NpgsqlException</c> from a bare
/// unit test is therefore not practical, so <see cref="PostgresDataContext.CreateError"/> is instead
/// exercised end-to-end by the Task 9 PostgreSQL round-trip error-path fact (a REAL NpgsqlException
/// raised by a live connection). This test covers the other half of <see cref="PostgresDataContext"/>'s
/// dialect-specific surface: the <c>NormalizeType</c> provider-type-name -> canonical C# routing token
/// map, via the same <c>NormalizeTypeForTest</c> internal seam <c>KeyParityTests</c> uses for SQL Server
/// and SQLite.
/// </para>
/// </summary>
public class PostgresCreateErrorTests
{
    // Minimal concrete context so we can reach the protected/internal members.
    private sealed class TestContext(IConfiguration configuration) : PostgresDataContext(configuration)
    {
    }

    [Theory]
    [InlineData("integer", "int")]
    [InlineData("int4", "int")]
    [InlineData("bigint", "long")]
    [InlineData("smallint", "short")]
    [InlineData("text", "string")]
    [InlineData("character varying", "string")]
    [InlineData("numeric", "decimal")]
    [InlineData("boolean", "bool")]
    [InlineData("uuid", "System.Guid")]
    [InlineData("date", "System.DateOnly")]
    [InlineData("time without time zone", "System.TimeOnly")]
    [InlineData("timestamp without time zone", "System.DateTime")]
    [InlineData("timestamp with time zone", "System.DateTimeOffset")]
    [InlineData("bytea", "byte[]")]
    [InlineData("real", "float")]
    [InlineData("double precision", "double")]
    public void NormalizeType_maps_postgres_provider_type_names_to_canonical_tokens(string providerTypeName, string expected)
    {
        var ctx = new TestContext(new ConfigurationBuilder().Build());

        Assert.Equal(expected, ctx.NormalizeTypeForTest(providerTypeName));
    }

    [Fact]
    public void NormalizeType_passes_through_unknown_types_lower_cased()
    {
        var ctx = new TestContext(new ConfigurationBuilder().Build());

        Assert.Equal("some_custom_domain", ctx.NormalizeTypeForTest("SOME_CUSTOM_DOMAIN"));
    }
}
