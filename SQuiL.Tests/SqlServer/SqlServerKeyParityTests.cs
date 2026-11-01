namespace SQuiL.Tests.SqlServer;

using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using SQuiL.Dialects;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

/// <summary>
/// The live-reader counterpart of <c>KeyParityTests.AssertParity</c> for SQL Server, like
/// <c>PostgresKeyParityTests</c> for PostgreSQL. It guards parity between the build-time shape key
/// (<c>SQuiLShapeKey</c>) and the runtime one (<c>SqlServerDataContext.NormalizeType</c>), taking the
/// provider type name from a real <see cref="SqlDataReader.GetDataTypeName"/> instead of a typed-in
/// literal.
///
/// <para>
/// <c>KeyParityTests</c> feeds <c>NormalizeType</c> the spellings SQL Server is expected to report.
/// This class pins them against a real server: if one differs, a test fails and shows the real
/// string. Fix <c>SqlServerDataContext.NormalizeType</c> in that case, not the test.
/// </para>
/// </summary>
[Collection("SqlServer container")]
public class SqlServerKeyParityTests(SqlServerContainerFixture fixture)
{
	private static readonly SqlServerDialect Dialect = new();

	private sealed class SqlServerProbe() : SqlServerDataContext(new ConfigurationBuilder().Build());

	private void SkipIfUnavailable()
	{
		if (fixture.StartupFailure is { } ex)
			Assert.Skip($"No SQL Server available for the SQL Server live-reader parity tests: {ex.Message}");
	}

	/// <summary>
	/// Table-column parity: parses a one-column table declare, takes the build-time routing token,
	/// declares a real table variable with the exact column type the generator emits
	/// (<c>Token.Original</c>), reads what the live reader reports for it, and compares.
	/// </summary>
	private async Task AssertParityAsync(string sqlType)
	{
		var tokens = SQuiLTokenizer.GetTokens($"Declare @Returns_T table(C {sqlType});\nUse [Db];\nSelect 1;");
		var blocks = SQuiLParser.ParseTokens(tokens);
		var block = blocks.Find(b => b.IsTable || b.IsObject);
		Assert.NotNull(block);

		var buildToken = TokenOf(SQuiLShapeKey.ShapeKeyOf(block!));

		var emittedDeclType = block!.Properties[0].Type.Original!;
		var providerTypeName = await ReadProviderTypeNameAsync($"Declare @p table(c {emittedDeclType}); Select c From @p;");

		Assert.Equal(buildToken, new SqlServerProbe().NormalizeTypeForTest(providerTypeName));
	}

	/// <summary>
	/// Scalar parity: a scalar output reaches the reader as <c>Select @Return_V As [V]</c>, so the
	/// live type name is read from that select after the declaration the generator emits.
	/// </summary>
	private async Task AssertScalarParityAsync(string sqlType)
	{
		var tokens = SQuiLTokenizer.GetTokens($"Declare @Return_V {sqlType};\nUse [Db];\nSelect @Return_V As V;");
		var blocks = SQuiLParser.ParseTokens(tokens);
		var block = blocks.Find(b => b.CodeType == CodeType.OUTPUT_VARIABLE);
		Assert.NotNull(block);

		var buildToken = TokenOf(SQuiLShapeKey.ScalarKeyOf(block!, Dialect));

		// SqlServerDialect.ScalarVariableDeclaration emits `Declare {DatabaseType.Original};`, where
		// Original is the whole `@Return_V <type>` declaration. Run exactly that.
		var emittedDeclaration = block!.DatabaseType.Original!;
		var providerTypeName = await ReadProviderTypeNameAsync($"Declare {emittedDeclaration}; Select @Return_V As [V];");

		Assert.Equal(buildToken, new SqlServerProbe().NormalizeTypeForTest(providerTypeName));
	}

	/// <summary>Takes the routing token after the colon in a <c>"name:token"</c> shape key.</summary>
	private static string TokenOf(string shapeKey)
	{
		var colonIdx = shapeKey.IndexOf(':');
		Assert.True(colonIdx >= 0, $"Shape key has an unexpected format: '{shapeKey}'");
		return shapeKey.Substring(colonIdx + 1);
	}

	/// <summary>
	/// Runs <paramref name="batch"/> (which selects one column, with or without rows) on its own
	/// connection and returns the live <c>GetDataTypeName(0)</c>, the exact input the runtime router
	/// (<c>SQuiLBaseDataContext.ShapeKey</c>) sees.
	/// </summary>
	private async Task<string> ReadProviderTypeNameAsync(string batch)
	{
		await using var connection = new SqlConnection(fixture.ConnectionString);
		await connection.OpenAsync(TestContext.Current.CancellationToken);

		await using var command = connection.CreateCommand();
		command.CommandText = batch;
		await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
		Assert.Equal(1, reader.FieldCount);
		return reader.GetDataTypeName(0);
	}

	// The same types KeyParityTests checks with typed-in spellings, here against a real server.

	[Fact] public Task Live_Parity_Bit() => RunAsync("bit");
	[Fact] public Task Live_Parity_Int() => RunAsync("int");
	[Fact] public Task Live_Parity_Bigint() => RunAsync("bigint");
	[Fact] public Task Live_Parity_Smallint() => RunAsync("smallint");
	[Fact] public Task Live_Parity_Tinyint() => RunAsync("tinyint");
	[Fact] public Task Live_Parity_Decimal() => RunAsync("decimal(18,2)");
	[Fact] public Task Live_Parity_Money() => RunAsync("money");
	[Fact] public Task Live_Parity_Smallmoney() => RunAsync("smallmoney");
	[Fact] public Task Live_Parity_Float() => RunAsync("float");
	[Fact] public Task Live_Parity_Real() => RunAsync("real");
	[Fact] public Task Live_Parity_Varchar() => RunAsync("varchar(100)");
	[Fact] public Task Live_Parity_Nvarchar() => RunAsync("nvarchar(50)");
	[Fact] public Task Live_Parity_Xml() => RunAsync("xml");
	[Fact] public Task Live_Parity_Date() => RunAsync("date");
	[Fact] public Task Live_Parity_Time() => RunAsync("time");
	[Fact] public Task Live_Parity_Datetime() => RunAsync("datetime");
	[Fact] public Task Live_Parity_Datetime2() => RunAsync("datetime2");
	[Fact] public Task Live_Parity_Smalldatetime() => RunAsync("smalldatetime");
	[Fact] public Task Live_Parity_Datetimeoffset() => RunAsync("datetimeoffset");
	[Fact] public Task Live_Parity_Uniqueidentifier() => RunAsync("uniqueidentifier");
	[Fact] public Task Live_Parity_Varbinary() => RunAsync("varbinary(max)");
	[Fact] public Task Live_Parity_Image() => RunAsync("image");
	[Fact] public Task Live_Parity_Timestamp() => RunAsync("timestamp");

	[Fact] public Task Live_ScalarParity_Int() => RunScalarAsync("int");
	[Fact] public Task Live_ScalarParity_Varchar() => RunScalarAsync("varchar(10)");
	[Fact] public Task Live_ScalarParity_Nvarchar() => RunScalarAsync("nvarchar(10)");
	[Fact] public Task Live_ScalarParity_Bit() => RunScalarAsync("bit");
	[Fact] public Task Live_ScalarParity_Datetime() => RunScalarAsync("datetime");
	[Fact] public Task Live_ScalarParity_Uniqueidentifier() => RunScalarAsync("uniqueidentifier");
	[Fact] public Task Live_ScalarParity_Decimal() => RunScalarAsync("decimal(18,2)");

	private async Task RunAsync(string sqlType)
	{
		SkipIfUnavailable();
		await AssertParityAsync(sqlType);
	}

	private async Task RunScalarAsync(string sqlType)
	{
		SkipIfUnavailable();
		await AssertScalarParityAsync(sqlType);
	}
}
