namespace SQuiL.Tests.Library;

using Microsoft.Extensions.Configuration;

using SQuiL;

using System.Data;
using System.Data.Common;
using System.Text;

internal class SQuiLBaseDataContextTestDouble(IConfiguration config) : SQuiLBaseDataContext(config)
{
	public string EnvironmentNameValue => EnvironmentName;

	public DbParameter Parameter(string name, SqlDbType type, object? value)
		=> CreateParameter(name, type, value);

	public void Add(StringBuilder query, List<DbParameter> parameters, int index,
		string table, string name, SqlDbType type, object? value, int size = 0)
		=> AddParams(query, parameters, index, table, name, type, value, size);
}

public class SQuiLBaseDataContextTests
{
	private static SQuiLBaseDataContextTestDouble Context(params (string Key, string? Value)[] settings)
		=> new(new ConfigurationBuilder()
			.AddInMemoryCollection(settings.ToDictionary(p => p.Key, p => p.Value))
			.Build());

	[Fact]
	public void CreateParameterMapsNullToDBNull()
		=> Assert.Equal(DBNull.Value, Context().Parameter("@P", SqlDbType.Int, null).Value);

	[Fact]
	public void AddParamsAppendsPositionalNameAndParameter()
	{
		StringBuilder query = new();
		List<DbParameter> parameters = [];

		Context().Add(query, parameters, 3, "Params_People", "UserID", SqlDbType.Int, 42);

		Assert.Equal("@Params_People_3_UserID", query.ToString());
		var p = Assert.Single(parameters);
		Assert.Equal("@Params_People_3_UserID", p.ParameterName);
		Assert.Equal(42, p.Value);
	}

	[Fact]
	public void AddParamsStringWithinSizePasses()
	{
		StringBuilder query = new();
		List<DbParameter> parameters = [];

		Context().Add(query, parameters, 0, "T", "Name", SqlDbType.VarChar, "abc", size: 5);

		var p = Assert.Single(parameters);
		Assert.Equal(5, p.Size);
		Assert.Equal("abc", p.Value);
	}

	[Fact]
	public void AddParamsStringOverSizeThrowsWithIndexAndName()
	{
		var ex = Assert.Throws<Exception>(() =>
			Context().Add(new(), [], 2, "T", "Name", SqlDbType.VarChar, "toolong", size: 3));

		Assert.Contains("index [2]", ex.Message);
		Assert.Contains("[Name]", ex.Message);
		Assert.Contains("more than 3 characters", ex.Message);
	}

	[Fact]
	public void AddParamsNullWithSizeBecomesDBNull()
	{
		List<DbParameter> parameters = [];

		Context().Add(new(), parameters, 0, "T", "Name", SqlDbType.VarChar, null, size: 5);

		Assert.Equal(DBNull.Value, Assert.Single(parameters).Value);
	}

	[Fact]
	public void EnvironmentNameConfigKeyWins()
		=> Assert.Equal("Staging", Context(("EnvironmentName", "Staging")).EnvironmentNameValue);

	[Fact]
	public void EnvironmentNameDefaultsToDevelopment()
		// Point the env-var fallback at a variable that cannot exist so the
		// machine's real ASPNETCORE_ENVIRONMENT cannot leak into the test.
		=> Assert.Equal("Development",
			Context(("EnvironmentVariable", $"SQUIL_TEST_{Guid.NewGuid():N}")).EnvironmentNameValue);

	[Fact]
	public void EnvironmentNameFallsBackToNamedEnvironmentVariable()
	{
		var variable = $"SQUIL_TEST_{Guid.NewGuid():N}";
		Environment.SetEnvironmentVariable(variable, "Beta");
		try
		{
			Assert.Equal("Beta", Context(("EnvironmentVariable", variable)).EnvironmentNameValue);
		}
		finally
		{
			Environment.SetEnvironmentVariable(variable, null);
		}
	}
}
