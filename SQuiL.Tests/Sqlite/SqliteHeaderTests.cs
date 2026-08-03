using System.Linq;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

namespace SQuiL.Tests.Sqlite;

/// <summary>
/// Task 5 (Phase 3B): the SQLite header model — <c>Create Temp Table</c> declarations replace
/// T-SQL <c>Declare</c>/<c>Use</c>, direction+cardinality is carried by the bare table name
/// (<c>Params_</c>/<c>Param_</c>/<c>Returns_</c>/<c>Return_</c>, same convention as the <c>@</c>-
/// prefixed T-SQL form), and the body boundary is positional: the first statement that is
/// neither a temp-table create nor a population of a declared PARAM temp table.
///
/// Per the Task 5/6 coupling note (see task-5-brief.md), <c>ShredStatement</c>/<c>ShredParamName</c>
/// stay stubbed (throw) until Task 6, so these tests assert at the PARSER level (the produced
/// <see cref="CodeBlock"/>s) rather than full code generation for INPUT tables. The one exception
/// is <see cref="Generation.OutputOnlyQueryGeneratesFullContext"/>, an OUTPUT-only snapshot that
/// needs no shred.
/// </summary>
public class SqliteHeaderTests
{
	private static readonly ISqlDialect Dialect = new SqliteDialect();

	private static System.Collections.Generic.List<CodeBlock> Parse(string sql)
		=> SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, Dialect), Dialect);

	[Fact]
	public void Params_prefix_multi_column_is_input_list()
	{
		var blocks = Parse("""
			Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT);
		Assert.Equal(CodeType.INPUT_TABLE, block.CodeType);
		Assert.Equal("Person", block.Name);
		Assert.Equal(3, block.Properties.Count);
	}

	[Fact]
	public void Param_prefix_multi_column_is_input_object()
	{
		var blocks = Parse("""
			Create Temp Table Param_Address (Street TEXT, City TEXT);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT);
		Assert.Equal(CodeType.INPUT_OBJECT, block.CodeType);
		Assert.Equal("Address", block.Name);
		Assert.Equal(2, block.Properties.Count);
	}

	[Fact]
	public void Param_prefix_single_column_collapses_to_input_scalar()
	{
		var blocks = Parse("""
			Create Temp Table Param_Age (Age INTEGER);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT);
		Assert.Equal(CodeType.INPUT_ARGUMENT, block.CodeType);
		Assert.Equal("Age", block.Name);
		Assert.Equal(TokenType.TYPE_BIGINT, block.DatabaseType.Type);
	}

	[Fact]
	public void Returns_prefix_single_column_stays_output_list_not_scalar()
	{
		var blocks = Parse("""
			Create Temp Table Returns_ID (ID INTEGER);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT);
		Assert.Equal(CodeType.OUTPUT_TABLE, block.CodeType);
		Assert.Equal("ID", block.Name);
		Assert.Equal(1, block.Properties.Count);
	}

	[Fact]
	public void Return_prefix_single_column_collapses_to_output_scalar()
	{
		var blocks = Parse("""
			Create Temp Table Return_Total (Total INTEGER);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT);
		Assert.Equal(CodeType.OUTPUT_VARIABLE, block.CodeType);
		Assert.Equal("Total", block.Name);
		Assert.Equal(TokenType.TYPE_BIGINT, block.DatabaseType.Type);
	}

	[Fact]
	public void Return_prefix_multi_column_stays_output_object()
	{
		var blocks = Parse("""
			Create Temp Table Return_Summary (RowCount INTEGER, Total INTEGER);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT);
		Assert.Equal(CodeType.OUTPUT_OBJECT, block.CodeType);
		Assert.Equal("Summary", block.Name);
		Assert.Equal(2, block.Properties.Count);
	}

	[Fact]
	public void No_using_block_is_ever_produced_for_sqlite()
	{
		var blocks = Parse("""
			Create Temp Table Return_Total (Total INTEGER);
			Select 1;
			""");

		Assert.DoesNotContain(blocks, b => b.CodeType == CodeType.USING);
	}

	/// <summary>
	/// The brief's worked example: sample DML into a declared PARAM table is dropped; a DML
	/// statement into a declared RETURN table (real production logic, not sample data) starts
	/// the body instead. Both temp-table declarations remain header declarations regardless of
	/// how many statements precede or follow them.
	/// </summary>
	[Fact]
	public void Boundary_drops_param_table_sample_dml_but_not_a_return_table_insert()
	{
		var blocks = Parse("""
			Create Temp Table Params_Person (PersonID INTEGER Primary Key, Name TEXT, Age INTEGER);
			Create Temp Table Returns_Imported (PersonID INTEGER, Status TEXT);
			Insert Into Params_Person (PersonID, Name, Age) Values (1, 'Ada', 36);
			Insert Into Returns_Imported (PersonID, Status) Select PersonID, 'ok' From Params_Person;
			Select PersonID, Status From Returns_Imported;
			""");

		Assert.Equal(2, blocks.Count(b => b.CodeType is CodeType.INPUT_TABLE or CodeType.OUTPUT_TABLE));

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);

		// The sample-data insert into the PARAM table never appears in the body at all.
		Assert.DoesNotContain("Values (1, 'Ada', 36)", body.Name);

		// The body starts at the Insert into the RETURN table (real logic) and carries
		// everything after it through to end of file, verbatim.
		Assert.StartsWith("Insert Into Returns_Imported", body.Name);
		Assert.Contains("Select PersonID, Status From Returns_Imported;", body.Name);
	}

	/// <summary>Covers the <c>Update</c> verb (not just <c>Insert</c>) for sample-DML dropping,
	/// and proves the drop applies even though the single-column PARAM table collapses to a
	/// scalar at the parser level — the tokenizer tracks the declared name regardless of the
	/// column count, since collapsing happens one layer up (in the parser).</summary>
	[Fact]
	public void Update_against_a_param_table_is_dropped_as_sample_dml()
	{
		var blocks = Parse("""
			Create Temp Table Param_Config (Flag INTEGER);
			Update Param_Config Set Flag = 1;
			Select Flag From Param_Config;
			""");

		var scalar = Assert.Single(blocks, b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT);
		Assert.Equal(CodeType.INPUT_ARGUMENT, scalar.CodeType);

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.DoesNotContain("Update", body.Name);
		Assert.StartsWith("Select Flag From Param_Config;", body.Name);
	}

	/// <summary>Covers the <c>Delete</c> verb for sample-DML dropping.</summary>
	[Fact]
	public void Delete_against_a_param_table_is_dropped_as_sample_dml()
	{
		var blocks = Parse("""
			Create Temp Table Params_Row (RowID INTEGER Primary Key, Note TEXT);
			Delete Params_Row Where RowID = 1;
			Select RowID, Note From Params_Row;
			""");

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.DoesNotContain("Delete", body.Name);
		Assert.StartsWith("Select RowID, Note From Params_Row;", body.Name);
	}

	/// <summary>Divergence #1 (Task A boundary parity): <c>Delete From &lt;ParamTable&gt;</c> — the
	/// <c>From</c> is OPTIONAL after <c>Delete</c> (mirrors the editors' <c>DELETE\s+FROM|DELETE</c>
	/// regex). Before the fix the tokenizer captured <c>From</c> as the table name, failed the
	/// param-table membership test, and treated the whole statement (and everything after it) as
	/// BODY one statement too early.</summary>
	[Fact]
	public void Delete_From_against_a_param_table_is_dropped_as_sample_dml()
	{
		var blocks = Parse("""
			Create Temp Table Params_Row (RowID INTEGER Primary Key, Note TEXT);
			Delete From Params_Row Where RowID = 1;
			Select RowID, Note From Params_Row;
			""");

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.DoesNotContain("Delete", body.Name);
		Assert.StartsWith("Select RowID, Note From Params_Row;", body.Name);
	}

	/// <summary>Divergence #4 (Task A boundary parity): a temp table whose prefix merely STARTS WITH
	/// <c>param</c> (e.g. <c>Parameter_Foo</c>) is NOT a Param_/Params_ input table and must NOT be
	/// added to the sample-DML-droppable param-table set. The membership test now matches the exact
	/// <c>^params?_</c> convention (matching the editors). Before the fix, <c>Parameter_Foo</c>
	/// loosely matched and its population <c>Insert</c> was wrongly dropped, starting the body late.</summary>
	[Fact]
	public void Loosely_prefixed_parameter_table_is_not_sample_dml_droppable()
	{
		var blocks = Parse("""
			Create Temp Table Parameter_Foo (ID INTEGER Primary Key, Note TEXT);
			Insert Into Parameter_Foo (ID, Note) Values (1, 'x');
			Select ID, Note From Parameter_Foo;
			""");

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		// `Parameter_` is not exactly `Param_`/`Params_`, so its Insert is real body logic, not
		// droppable sample DML — the body begins there.
		Assert.StartsWith("Insert Into Parameter_Foo", body.Name);
	}

	public class Generation
	{
		/// <summary>
		/// The ONE generation snapshot this task adds (per the Task 5/6 coupling note): a purely
		/// OUTPUT-shaped SQLite query — a <c>Returns_</c> list plus a <c>Return_</c> scalar, no
		/// input table — exercises <c>SqliteDialect.TableVariableDeclaration</c>, the positional
		/// body boundary, and a full emitted context, all without touching the still-stubbed
		/// <c>ShredStatement</c>/<c>ShredParamName</c> path (which only INPUT tables/objects need).
		/// </summary>
		[Fact]
		public System.Threading.Tasks.Task OutputOnlyQueryGeneratesFullContext()
		{
			var name = nameof(OutputOnlyQueryGeneratesFullContext);
			return TestHelper.VerifySqlite([TestHelper.TestHeaderSqlite([name])], [$$"""
				--Name: {{name}}
				Create Temp Table Returns_Person (PersonID INTEGER Primary Key, Name TEXT);
				Create Temp Table Return_Total (Total INTEGER);
				Insert Into Returns_Person (PersonID, Name) Select 1, 'Ada';
				Insert Into Return_Total (Total) Select Count(*) From Returns_Person;
				Select PersonID, Name From Returns_Person;
				Select Total From Return_Total;
				"""]);
		}

		/// <summary>
		/// Task 6: an INPUT-table SQLite query now generates a FULL context (the shred is no longer
		/// stubbed). Exercises <c>SqliteDialect.ShredStatement</c>/<c>ShredParamName</c> inside the
		/// emitted <c>input&lt;Name&gt;</c> helper — the sample-DML <c>Insert</c> into the PARAM table
		/// is dropped (Task 5 boundary), the shred is emitted as <c>json_each</c>/<c>json_extract</c>,
		/// and the BLOB column is decoded with <c>unhex(…)</c> (the Task 6 blob decision). The
		/// generated code also Tier-0 compiles against <c>SQuiL.Sqlite</c>.
		/// </summary>
		[Fact]
		public System.Threading.Tasks.Task InputTableQueryGeneratesJsonEachShred()
		{
			var name = nameof(InputTableQueryGeneratesJsonEachShred);
			return TestHelper.VerifySqlite([TestHelper.TestHeaderSqlite([name])], [$$"""
				--Name: {{name}}
				Create Temp Table Params_Doc (DocID INTEGER Primary Key, Title TEXT, Payload BLOB);
				Create Temp Table Returns_Imported (DocID INTEGER, Title TEXT);
				Insert Into Params_Doc (DocID, Title, Payload) Values (1, 'Ada', unhex('00AB'));
				Insert Into Returns_Imported (DocID, Title) Select DocID, Title From Params_Doc;
				Select DocID, Title From Returns_Imported;
				"""]);
		}
	}
}
