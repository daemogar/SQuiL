using SQuiL.Dialects;

namespace SQuiL.Tests.Dialects;

public class SqlServerDialectTests
{
	private readonly SqlServerDialect _dialect = new();

	[Fact]
	public void ProviderUsingDirective_IsSqlClient()
		=> Assert.Equal("using Microsoft.Data.SqlClient;", _dialect.ProviderUsingDirective());

	[Fact]
	public void ProviderExceptionType_IsSqlException()
		=> Assert.Equal("SqlException", _dialect.ProviderExceptionType());

	[Fact]
	public void DatabaseDirective_EmitsUseStatement()
		=> Assert.Equal("Use [MyDb];", _dialect.DatabaseDirective("MyDb"));
}
