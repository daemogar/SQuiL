using SQuiL.Dialects;
using SQuiL.Tokenizer;

using Xunit;

namespace SQuiL.Tests.Dialects;

/// <summary>
/// Exercises the PostgreSQL type map end-to-end for Task 4: (1) the tokenizer classifies every
/// PostgreSQL type spelling (bare keywords via <c>PostgresTypeRegex</c>, multi-word spellings via
/// <c>PostgresMultiWordTypeRegex</c>, and the shared T-SQL <c>TypeRegex</c> spellings that need no
/// PostgreSQL-specific fork) to the correct <see cref="TokenType"/> and C# type; (2)
/// <see cref="PostgresDialect.ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem)"/>/
/// <see cref="PostgresDialect.ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock)"/>/
/// <see cref="PostgresDialect.ParamTypeExpr"/> produce the right Npgsql-facing strings. See
/// task-4-brief.md §3 for the full spelling -> TokenType mapping table this mirrors.
/// Parses via PostgreSQL's real Create-Temp-Table header (shared with SQLite, Task 5) — a
/// `Returns_`-prefixed multi-column declaration stays a list (<see cref="CodeType.OUTPUT_TABLE"/>),
/// so `block.Properties[0]` is the declared column, unaffected by the single-column-collapses-to-
/// scalar rule (which only fires for the singular `Return_`/`Param_` object form).
/// </summary>
public class PostgresTypeMapTests
{
	private readonly PostgresDialect _dialect = new();

	[Theory]
	// int family
	[InlineData("int4", TokenType.TYPE_INT, "int")]
	[InlineData("int", TokenType.TYPE_INT, "int")]
	[InlineData("integer", TokenType.TYPE_INT, "int")]
	[InlineData("int8", TokenType.TYPE_BIGINT, "long")]
	[InlineData("bigint", TokenType.TYPE_BIGINT, "long")]
	[InlineData("int2", TokenType.TYPE_SMALLINT, "short")]
	[InlineData("smallint", TokenType.TYPE_SMALLINT, "short")]
	// string family
	[InlineData("text", TokenType.TYPE_STRING, "string")]
	[InlineData("varchar(50)", TokenType.TYPE_STRING, "string")]
	[InlineData("character varying", TokenType.TYPE_STRING, "string")]
	[InlineData("character varying(50)", TokenType.TYPE_STRING, "string")]
	[InlineData("char(1)", TokenType.TYPE_STRING, "string")]
	[InlineData("bpchar", TokenType.TYPE_STRING, "string")]
	[InlineData("json", TokenType.TYPE_STRING, "string")]
	[InlineData("jsonb", TokenType.TYPE_STRING, "string")]
	// binary / guid / boolean
	[InlineData("bytea", TokenType.TYPE_VARBINARY, "byte[]")]
	[InlineData("uuid", TokenType.TYPE_GUID, "System.Guid")]
	[InlineData("bool", TokenType.TYPE_BOOLEAN, "bool")]
	[InlineData("boolean", TokenType.TYPE_BOOLEAN, "bool")]
	// date/time family — note the deliberate NO-forks: `date`/`real` fall through to the shared
	// T-SQL default (TYPE_DATE/TYPE_FLOAT), unlike SQLite which forks both.
	[InlineData("date", TokenType.TYPE_DATE, "System.DateOnly")]
	[InlineData("time", TokenType.TYPE_TIME, "System.TimeOnly")]
	[InlineData("time without time zone", TokenType.TYPE_TIME, "System.TimeOnly")]
	[InlineData("timestamp", TokenType.TYPE_DATETIME, "System.DateTime")]
	[InlineData("timestamp without time zone", TokenType.TYPE_DATETIME, "System.DateTime")]
	[InlineData("timestamptz", TokenType.TYPE_DATETIMEOFFSET, "System.DateTimeOffset")]
	[InlineData("timestamp with time zone", TokenType.TYPE_DATETIMEOFFSET, "System.DateTimeOffset")]
	// numeric family
	[InlineData("numeric", TokenType.TYPE_DECIMAL, "decimal")]
	[InlineData("decimal", TokenType.TYPE_DECIMAL, "decimal")]
	[InlineData("real", TokenType.TYPE_FLOAT, "float")]
	[InlineData("float4", TokenType.TYPE_FLOAT, "float")]
	[InlineData("double precision", TokenType.TYPE_DOUBLE, "double")]
	[InlineData("float8", TokenType.TYPE_DOUBLE, "double")]
	[InlineData("money", TokenType.TYPE_MONEY, "decimal")]
	public void Spelling_tokenizes_to_expected_type(string pgType, TokenType expectedTokenType, string expectedCSharpType)
	{
		var block = PostgresDialectTestHelper.ParseSingleOutputTable(
			$"Create Temp Table Returns_T (C {pgType}); Select 1;");

		var column = Assert.Single(block.Properties);

		Assert.Equal(expectedTokenType, column.Type.Type);
		Assert.Equal(expectedCSharpType, column.CSharpType());
	}

	[Theory]
	[InlineData("int", "reader.GetInt32", "NpgsqlTypes.NpgsqlDbType.Integer")]
	[InlineData("bigint", "reader.GetInt64", "NpgsqlTypes.NpgsqlDbType.Bigint")]
	[InlineData("smallint", "reader.GetInt16", "NpgsqlTypes.NpgsqlDbType.Smallint")]
	[InlineData("text", "reader.GetString", "NpgsqlTypes.NpgsqlDbType.Text")]
	[InlineData("bytea", "reader.GetFieldValue<byte[]>", "NpgsqlTypes.NpgsqlDbType.Bytea")]
	[InlineData("uuid", "reader.GetGuid", "NpgsqlTypes.NpgsqlDbType.Uuid")]
	[InlineData("boolean", "reader.GetBoolean", "NpgsqlTypes.NpgsqlDbType.Boolean")]
	[InlineData("date", "reader.GetFieldValue<System.DateOnly>", "NpgsqlTypes.NpgsqlDbType.Date")]
	[InlineData("time", "reader.GetFieldValue<System.TimeOnly>", "NpgsqlTypes.NpgsqlDbType.Time")]
	[InlineData("timestamp", "reader.GetDateTime", "NpgsqlTypes.NpgsqlDbType.Timestamp")]
	[InlineData("timestamptz", "reader.GetFieldValue<System.DateTimeOffset>", "NpgsqlTypes.NpgsqlDbType.TimestampTz")]
	[InlineData("numeric", "reader.GetDecimal", "NpgsqlTypes.NpgsqlDbType.Numeric")]
	[InlineData("money", "reader.GetDecimal", "NpgsqlTypes.NpgsqlDbType.Numeric")]
	[InlineData("real", "reader.GetFloat", "NpgsqlTypes.NpgsqlDbType.Real")]
	[InlineData("double precision", "reader.GetDouble", "NpgsqlTypes.NpgsqlDbType.Double")]
	public void Scalar_ReaderAccessor_and_ParamTypeExpr(string pgType, string expectedReader, string expectedParam)
	{
		var block = PostgresDialectTestHelper.ParseSingleInputScalar(
			$"Create Temp Table Param_X (Value {pgType}); Select 1;");

		Assert.Equal(expectedReader, _dialect.ReaderAccessor(block));
		Assert.Equal(expectedParam, _dialect.ParamTypeExpr(block));
	}

	[Fact]
	public void TableColumn_ReaderAccessor_matches_scalar_mapping()
	{
		var block = PostgresDialectTestHelper.ParseSingleOutputTable(
			"Create Temp Table Returns_Rows (Value int, Other text); Select 1;");
		var column = Assert.Single(block.Properties, p => p.Identifier.Value == "Value");

		Assert.Equal("reader.GetInt32", _dialect.ReaderAccessor(column));
	}
}
