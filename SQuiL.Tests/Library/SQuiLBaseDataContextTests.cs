namespace SQuiL.Tests.Library;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using SQuiL;

using System.Data;
using System.Data.Common;

internal class SQuiLBaseDataContextTestDouble(IConfiguration config) : SQuiLBaseDataContext(config)
{
	public string EnvironmentNameValue => EnvironmentName;

	public DbParameter Parameter(string name, SqlDbType type, object? value)
		=> CreateParameter(name, type, value);

	public DbParameter AddJson(List<DbParameter> parameters, string name, object? value)
		=> AddJsonParameter(parameters, name, value);
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
	public void AddJsonParameterAddsSingleNVarCharMaxParameter()
	{
		List<DbParameter> parameters = [];

		var p = Context().AddJson(parameters, "@__json_Params_People",
			new[] { new { UserID = 1, Name = "Ada" } });

		Assert.Same(p, Assert.Single(parameters));
		Assert.Equal("@__json_Params_People", p.ParameterName);
		Assert.Equal(SqlDbType.NVarChar, ((SqlParameter)p).SqlDbType);
		Assert.Equal(-1, p.Size);
		Assert.Equal("""[{"UserID":1,"Name":"Ada"}]""", p.Value);
	}

	[Fact]
	public void SerializeUsesVerbatimPropertyNames()
		=> Assert.Equal("""{"UserID":7}""", SQuiLJson.Serialize(new { UserID = 7 }));

	[Fact]
	public void SerializeWritesBinaryAsBareHex()
		=> Assert.Equal("""{"Blob":"0A0B"}""",
			SQuiLJson.Serialize(new { Blob = new byte[] { 0x0A, 0x0B } }));

	[Fact]
	public void SerializeWritesNullColumnAsJsonNull()
		=> Assert.Equal("""{"Name":null}""",
			SQuiLJson.Serialize(new { Name = (string?)null }));

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

	[Theory]
	[InlineData("int", "int")]
	[InlineData("bit", "bool")]
	[InlineData("varchar", "string")]
	[InlineData("nvarchar", "string")]
	[InlineData("decimal", "decimal")]
	[InlineData("date", "System.DateOnly")]
	[InlineData("datetime", "System.DateTime")]
	[InlineData("datetime2", "System.DateTime")]
	[InlineData("datetimeoffset", "System.DateTimeOffset")]
	[InlineData("uniqueidentifier", "System.Guid")]
	[InlineData("varbinary", "byte[]")]
	[InlineData("time", "System.TimeOnly")]
	[InlineData("float", "double")]
	public void NormalizeType_MapsProviderNameToCanonicalToken(string provider, string expected)
		=> Assert.Equal(expected, SQuiLBaseDataContext.NormalizeTypeForTest(provider));

	[Fact]
	public void NormalizeType_UnknownTypePassesThroughLowercased()
		=> Assert.Equal("hierarchyid", SQuiLBaseDataContext.NormalizeTypeForTest("HierarchyID"));
}
