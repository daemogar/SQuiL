using SQuiL.Dialects;

using Xunit;

namespace SQuiL.Tests.Dialects;

/// <summary>
/// Exercises SqliteDialect.ReaderAccessor/ParamTypeExpr for every SQLite type keyword, on both
/// the scalar (CodeBlock) and table-column (CodeItem) overloads. See task-4-brief.md for the
/// design's §4.4 mapping table and the Task 4/5 ordering note on why these parse via the
/// ordinary Declare form instead of SQLite's real Create-Temp-Table header (Task 5).
/// </summary>
public class SqliteTypeMapTests
{
	private readonly SqliteDialect _dialect = new();

	[Theory]
	[InlineData("INTEGER", "reader.GetInt64", "Microsoft.Data.Sqlite.SqliteType.Integer")]
	[InlineData("TEXT", "reader.GetString", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("REAL", "reader.GetDouble", "Microsoft.Data.Sqlite.SqliteType.Real")]
	[InlineData("BLOB", "reader.GetFieldValue<byte[]>", "Microsoft.Data.Sqlite.SqliteType.Blob")]
	[InlineData("NUMERIC", "reader.GetDecimal", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("DECIMAL", "reader.GetDecimal", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("BOOLEAN", "reader.GetBoolean", "Microsoft.Data.Sqlite.SqliteType.Integer")]
	[InlineData("DATE", "reader.GetFieldValue<System.DateTime>", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("DATETIME", "reader.GetFieldValue<System.DateTime>", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("GUID", "reader.GetFieldValue<System.Guid>", "Microsoft.Data.Sqlite.SqliteType.Text")]
	[InlineData("UNIQUEIDENTIFIER", "reader.GetFieldValue<System.Guid>", "Microsoft.Data.Sqlite.SqliteType.Text")]
	public void Scalar_reader_and_param(string sqlType, string expectedReader, string expectedParam)
	{
		var block = SqliteDialectTestHelper.ParseSingleInputScalar(
			$"Declare @Param_X {sqlType}; Use [Db]; Select 1;");

		Assert.Equal(expectedReader, _dialect.ReaderAccessor(block));
		Assert.Equal(expectedParam, _dialect.ParamTypeExpr(block));
	}

	[Fact]
	public void Integer_reader_and_param()
	{
		var block = SqliteDialectTestHelper.ParseSingleInputScalar(
			"Declare @Param_X INTEGER; Use [Db]; Select 1;");
		Assert.Equal("reader.GetInt64", _dialect.ReaderAccessor(block));
		Assert.Equal("Microsoft.Data.Sqlite.SqliteType.Integer", _dialect.ParamTypeExpr(block));
	}

	[Fact]
	public void TableColumn_ReaderAccessor_matches_scalar_mapping()
	{
		var block = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Declare @Params_Rows table(Value INTEGER); Use [Db]; Select 1;");
		var column = Assert.Single(block.Properties);

		Assert.Equal("reader.GetInt64", _dialect.ReaderAccessor(column));
	}
}
