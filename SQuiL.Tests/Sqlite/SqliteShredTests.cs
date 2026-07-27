using SQuiL.Dialects;
using SQuiL.Tests.Dialects;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 6 (Phase 3B): the SQLite input-marshalling shred — the <c>json_each</c> / <c>json_extract</c>
/// analogue of SQL Server's <c>OpenJson … With (…)</c> shred (see
/// <see cref="SQuiL.Tests.ParamSharding.ShredSqlTests"/>). The insert target is the FULL
/// (unstripped) temp-table name the SQLite header create used (<c>Params_</c>/<c>Param_</c>),
/// not the stripped base name; column values are pulled with <c>json_extract(value, '$.Col')</c>.
///
/// Blob columns are decoded with <c>unhex(…)</c>: the shared <see cref="SQuiLBinaryJsonConverter"/>
/// (via <c>SQuiLJson.Serialize</c>, called by <c>SqliteDataContext.AddJsonParameter</c>) already
/// serialises <see cref="byte"/>[] as bare uppercase hex, and SQLite's <c>unhex()</c> (3.41+,
/// bundle ships 3.49.1) decodes it back to a blob — the SQLite mirror of SQL Server's
/// <c>Convert(varbinary(N), …, 2)</c>. The runtime round-trip is asserted in Task 9's
/// <c>SqliteBlobRoundTripTests</c>.
/// </summary>
public class SqliteShredTests
{
	private static readonly SqliteDialect Dialect = new();

	[Fact]
	public void Table_shred_emits_json_each_insert_select()
	{
		var block = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Person (PersonID INTEGER, Name TEXT, Age INTEGER);");
		var sql = Dialect.ShredStatement(block);

		Assert.StartsWith("Insert Into Params_Person([PersonID], [Name], [Age])", sql);
		Assert.Contains("From json_each(@__json_Params_Person)", sql);
		Assert.Contains("json_extract(value, '$.PersonID')", sql);
		Assert.Contains("json_extract(value, '$.Name')", sql);
		Assert.Contains("json_extract(value, '$.Age')", sql);
		Assert.DoesNotContain("Values", sql);   // no VALUES list
		Assert.DoesNotContain("OpenJson", sql);  // not the SQL Server shred
	}

	[Fact]
	public void Object_shred_uses_singular_param_name_and_full_table_name()
	{
		var block = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Param_Address (Street TEXT, City TEXT);");
		var sql = Dialect.ShredStatement(block);

		Assert.StartsWith("Insert Into Param_Address([Street], [City])", sql);
		Assert.Contains("From json_each(@__json_Param_Address)", sql);
	}

	[Fact]
	public void ShredParamName_is_plural_for_table_and_singular_for_object()
	{
		var table = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Person (PersonID INTEGER, Name TEXT);");
		var obj = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Param_Address (Street TEXT, City TEXT);");

		Assert.Equal("@__json_Params_Person", Dialect.ShredParamName(table));
		Assert.Equal("@__json_Param_Address", Dialect.ShredParamName(obj));
	}

	/// <summary>
	/// The blob decision: a BLOB column (tokenizes to <c>TYPE_VARBINARY</c>) is decoded with
	/// <c>unhex(json_extract(…))</c>, while a non-blob column stays a bare <c>json_extract(…)</c>.
	/// </summary>
	[Fact]
	public void Blob_column_is_decoded_with_unhex()
	{
		var block = SqliteDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Doc (DocID INTEGER, Payload BLOB);");
		var sql = Dialect.ShredStatement(block);

		Assert.Contains("unhex(json_extract(value, '$.Payload'))", sql);
		Assert.Contains("json_extract(value, '$.DocID')", sql);
		Assert.DoesNotContain("unhex(json_extract(value, '$.DocID'))", sql);
	}
}
