using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SQuiL;
using Xunit;

namespace SQuiL.Tests.Dialects;

public class SqliteCreateErrorTests
{
    // Minimal concrete context so we can reach the protected CreateError.
    private sealed class TestContext(IConfiguration configuration) : SqliteDataContext(configuration)
    {
        public SQuiLError Expose(SqliteException e) => CreateError(e);
    }

    [Fact]
    public void CreateError_maps_SqliteException_fields()
    {
        var ctx = new TestContext(new ConfigurationBuilder().Build());
        // SqliteException(message, errorCode, extendedErrorCode)
        var ex = new SqliteException("near \"SELCT\": syntax error", 1, 1);

        var error = ctx.Expose(ex);

        Assert.Equal(1, error.Number);          // SqliteErrorCode -> Number
        Assert.Equal("near \"SELCT\": syntax error", error.Message);
        Assert.IsAssignableFrom<DbException>(error.AsDbException());
    }
}
