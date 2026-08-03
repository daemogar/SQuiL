using SQuiL.Dialects;

namespace SQuiL.Tests.Dialects;

public class SqlServerDialectTests
{
	private readonly SqlServerDialect _dialect = new();

	[Fact]
	public void UsingDirectives_IsSqlClientSingleton()
		=> Assert.Equal(new[] { "using Microsoft.Data.SqlClient;" }, _dialect.UsingDirectives());

	[Fact]
	public void ProviderExceptionType_IsSqlException()
		=> Assert.Equal("SqlException", _dialect.ProviderExceptionType());

	[Fact]
	public void DatabaseDirective_EmitsUseStatement()
		=> Assert.Equal("Use [MyDb];", _dialect.DatabaseDirective("MyDb"));

	[Fact]
	public void VarCharType_IsQualified()
		=> Assert.Equal("System.Data.SqlDbType.VarChar", _dialect.VarCharType());

	[Fact]
	public void BitType_IsQualified()
		=> Assert.Equal("System.Data.SqlDbType.Bit", _dialect.BitType());

	[Fact]
	public void ShredParamName_ListIsPlural()
	{
		var block = SqlServerDialectTestHelper.ParseSingleInputBlock(
			"Declare @Params_People table(PersonID int Primary Key, Name varchar(50)); Use [Db]; Select 1;");
		Assert.Equal("@__json_Params_People", _dialect.ShredParamName(block));
	}
}
