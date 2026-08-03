using SQuiL.Dialects;
using SQuiL.Tests.Dialects;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 6 (Phase 3 Postgres): the PostgreSQL input-marshalling shred — the <c>json_to_recordset</c>
/// analogue of SQL Server's <c>OpenJson … With (…)</c> shred (see
/// <see cref="SQuiL.Tests.ParamSharding.ShredSqlTests"/> and the SQLite twin
/// <see cref="SQuiL.Tests.Sqlite.SqliteShredTests"/>). Unlike SQLite's untyped <c>json_each</c>,
/// PostgreSQL's <c>json_to_recordset</c> needs an explicit, TYPED column list — declared via the
/// <c>AS x("Col" pgtype, …)</c> clause — to shred the JSON array into rows.
///
/// Insert target + insert column list stay BARE (Option B; PG folds unquoted identifiers to
/// lowercase, matching this dialect's bare DDL). The <c>AS x(...)</c> column list is the ONE place
/// this dialect quotes identifiers — required so PG matches the PascalCase JSON keys the shared
/// <c>SQuiLJson</c> serializer emits (PG would otherwise fold an unquoted <c>PersonID</c> to
/// <c>personid</c> and fail to bind the recordset column against the JSON key).
///
/// Blob columns are decoded with <c>decode(…, 'hex')</c>: the shared <c>SQuiLBinaryJsonConverter</c>
/// (via <c>SQuiLJson.Serialize</c>, called by <c>PostgresDataContext.AddJsonParameter</c>) already
/// serialises <see cref="byte"/>[] as bare uppercase hex, and PostgreSQL's <c>decode(text, 'hex')</c>
/// decodes it back to <c>bytea</c> — the PG mirror of SQL Server's <c>Convert(varbinary(N), …, 2)</c>
/// and SQLite's <c>unhex(…)</c>.
/// </summary>
public class PostgresShredTests
{
	private static readonly PostgresDialect Dialect = new();

	[Fact]
	public void Table_shred_emits_json_to_recordset_insert_select()
	{
		var block = PostgresDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Person (PersonID int4, Name text, Age int4);");
		var sql = Dialect.ShredStatement(block);

		Assert.StartsWith("Insert Into Params_Person(PersonID, Name, Age)", sql);
		Assert.Contains("From json_to_recordset(@__json_Params_Person) AS x(", sql);
		Assert.Contains("\"PersonID\" int4", sql);
		Assert.Contains("\"Name\" text", sql);
		Assert.Contains("\"Age\" int4", sql);
		Assert.Contains("x.\"PersonID\"", sql);
		Assert.Contains("x.\"Name\"", sql);
		Assert.Contains("x.\"Age\"", sql);
		Assert.DoesNotContain("Values", sql);       // no VALUES list
		Assert.DoesNotContain("OpenJson", sql);      // not the SQL Server shred
		Assert.DoesNotContain("json_each", sql);     // not the SQLite shred
	}

	[Fact]
	public void Object_shred_uses_singular_param_name_and_bare_table_name()
	{
		var block = PostgresDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Param_Address (Street text, City text);");
		var sql = Dialect.ShredStatement(block);

		Assert.StartsWith("Insert Into Param_Address(Street, City)", sql);
		Assert.Contains("From json_to_recordset(@__json_Param_Address) AS x(", sql);
		Assert.Contains("\"Street\" text", sql);
		Assert.Contains("\"City\" text", sql);
	}

	[Fact]
	public void ShredParamName_is_plural_for_table_and_singular_for_object()
	{
		var table = PostgresDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Person (PersonID int4, Name text);");
		var obj = PostgresDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Param_Address (Street text, City text);");

		Assert.Equal("@__json_Params_Person", Dialect.ShredParamName(table));
		Assert.Equal("@__json_Param_Address", Dialect.ShredParamName(obj));
	}

	/// <summary>
	/// The blob decision: a <c>bytea</c> column (tokenizes to <c>TYPE_VARBINARY</c>) is declared
	/// <c>text</c> in the typed AS-list and decoded with <c>decode(x."Col", 'hex')</c> in the
	/// SELECT, while a non-blob column keeps its author-declared PG type and stays a bare
	/// <c>x."Col"</c> reference.
	/// </summary>
	[Fact]
	public void Blob_column_is_declared_text_and_decoded_with_decode_hex()
	{
		var block = PostgresDialectTestHelper.ParseSingleInputBlock(
			"Create Temp Table Params_Doc (DocID int4, Payload bytea);");
		var sql = Dialect.ShredStatement(block);

		Assert.Contains("\"Payload\" text", sql);
		Assert.DoesNotContain("\"Payload\" bytea", sql);
		Assert.Contains("decode(x.\"Payload\", 'hex')", sql);
		Assert.Contains("x.\"DocID\"", sql);
		Assert.DoesNotContain("decode(x.\"DocID\"", sql);
	}

	public class Generation
	{
		/// <summary>
		/// Task 6: an INPUT-table PostgreSQL query now generates a FULL context (the shred is no
		/// longer stubbed). Exercises <c>PostgresDialect.ShredStatement</c>/<c>ShredParamName</c>
		/// inside the emitted <c>input&lt;Name&gt;</c> helper — the sample-DML <c>Insert</c> into the
		/// PARAM table is dropped (Task 5 boundary), the shred is emitted as
		/// <c>json_to_recordset</c>/typed <c>AS x(...)</c>, and the <c>bytea</c> column is decoded
		/// with <c>decode(…, 'hex')</c> (the Task 6 blob decision). The generated code also Tier-0
		/// compiles against <c>SQuiL.Postgres</c>.
		/// </summary>
		[Fact]
		public System.Threading.Tasks.Task InputTableQueryGeneratesJsonToRecordsetShred()
		{
			var name = nameof(InputTableQueryGeneratesJsonToRecordsetShred);
			return TestHelper.VerifyPostgres([TestHelper.TestHeaderPostgres([name])], [$$"""
				--Name: {{name}}
				Create Temp Table Params_Person (PersonID int4 Primary Key, Name text, Photo bytea);
				Create Temp Table Returns_Imported (PersonID int4, Name text);
				Insert Into Params_Person (PersonID, Name, Photo) Values (1, 'Ada', decode('00AB', 'hex'));
				Insert Into Returns_Imported (PersonID, Name) Select PersonID, Name From Params_Person;
				Select PersonID, Name From Returns_Imported;
				"""]);
		}
	}
}
