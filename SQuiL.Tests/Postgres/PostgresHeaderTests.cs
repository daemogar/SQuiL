using System.Linq;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using Xunit;

namespace SQuiL.Tests.Postgres;

/// <summary>
/// Task 5 (Phase 3 Postgres): the PostgreSQL header model — near-twin of <c>SqliteHeaderTests</c>.
/// <c>Create Temp Table</c> declarations replace T-SQL <c>Declare</c>/<c>Use</c>, direction+
/// cardinality is carried by the bare table name (<c>Params_</c>/<c>Param_</c>/<c>Returns_</c>/
/// <c>Return_</c>, same convention as the <c>@</c>-prefixed T-SQL form), and the body boundary is
/// positional: the first statement that is neither a temp-table create nor a population of a
/// declared PARAM temp table.
///
/// <c>ShredStatement</c>/<c>ShredParamName</c> stay stubbed (throw) until Task 6, so these tests
/// assert at the PARSER level (the produced <see cref="CodeBlock"/>s) rather than full code
/// generation for INPUT tables. The one exception is <see cref="Generation.OutputOnlyQueryGeneratesFullContext"/>,
/// an OUTPUT-only snapshot that needs no shred and exercises <see cref="PostgresDialect.TableVariableDeclaration"/>/
/// <see cref="PostgresDialect.ScalarVariableDeclaration"/> end-to-end.
/// </summary>
public class PostgresHeaderTests
{
	private static readonly ISqlDialect Dialect = new PostgresDialect();

	private static System.Collections.Generic.List<CodeBlock> Parse(string sql)
		=> SQuiLParser.ParseTokens(SQuiLTokenizer.GetTokens(sql, Dialect), Dialect);

	[Fact]
	public void Params_prefix_multi_column_is_input_list()
	{
		var blocks = Parse("""
			Create Temp Table Params_Person (PersonID int Primary Key, Name text, Age int);
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
			Create Temp Table Param_Address (Street text, City text);
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
			Create Temp Table Param_Age (Age int);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.INPUT) == CodeType.INPUT);
		Assert.Equal(CodeType.INPUT_ARGUMENT, block.CodeType);
		Assert.Equal("Age", block.Name);
		Assert.Equal(TokenType.TYPE_INT, block.DatabaseType.Type);
	}

	[Fact]
	public void Returns_prefix_single_column_stays_output_list_not_scalar()
	{
		var blocks = Parse("""
			Create Temp Table Returns_ID (ID int);
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
			Create Temp Table Return_Total (Total int);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT);
		Assert.Equal(CodeType.OUTPUT_VARIABLE, block.CodeType);
		Assert.Equal("Total", block.Name);
		Assert.Equal(TokenType.TYPE_INT, block.DatabaseType.Type);
	}

	[Fact]
	public void Return_prefix_multi_column_stays_output_object()
	{
		var blocks = Parse("""
			Create Temp Table Return_Summary (RowCount int, Total int);
			Select 1;
			""");

		var block = Assert.Single(blocks, b => (b.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT);
		Assert.Equal(CodeType.OUTPUT_OBJECT, block.CodeType);
		Assert.Equal("Summary", block.Name);
		Assert.Equal(2, block.Properties.Count);
	}

	[Fact]
	public void No_using_block_is_ever_produced_for_postgres()
	{
		var blocks = Parse("""
			Create Temp Table Return_Total (Total int);
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
			Create Temp Table Params_Person (PersonID int Primary Key, Name text, Age int);
			Create Temp Table Returns_Imported (PersonID int, Status text);
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
			Create Temp Table Param_Config (Flag int);
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
			Create Temp Table Params_Row (RowID int Primary Key, Note text);
			Delete Params_Row Where RowID = 1;
			Select RowID, Note From Params_Row;
			""");

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.DoesNotContain("Delete", body.Name);
		Assert.StartsWith("Select RowID, Note From Params_Row;", body.Name);
	}

	/// <summary>The <c>From</c> is OPTIONAL after <c>Delete</c> (mirrors the editors'
	/// <c>DELETE\s+FROM|DELETE</c> regex).</summary>
	[Fact]
	public void Delete_From_against_a_param_table_is_dropped_as_sample_dml()
	{
		var blocks = Parse("""
			Create Temp Table Params_Row (RowID int Primary Key, Note text);
			Delete From Params_Row Where RowID = 1;
			Select RowID, Note From Params_Row;
			""");

		var body = Assert.Single(blocks, b => b.CodeType == CodeType.BODY);
		Assert.DoesNotContain("Delete", body.Name);
		Assert.StartsWith("Select RowID, Note From Params_Row;", body.Name);
	}

	/// <summary>A temp table whose prefix merely STARTS WITH <c>param</c> (e.g. <c>Parameter_Foo</c>)
	/// is NOT a Param_/Params_ input table and must NOT be added to the sample-DML-droppable
	/// param-table set. The membership test matches the exact <c>^params?_</c> convention.</summary>
	[Fact]
	public void Loosely_prefixed_parameter_table_is_not_sample_dml_droppable()
	{
		var blocks = Parse("""
			Create Temp Table Parameter_Foo (ID int Primary Key, Note text);
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
		/// OUTPUT-shaped PostgreSQL query — a <c>Returns_</c> list plus a <c>Return_</c> scalar, no
		/// input table — exercises <c>PostgresDialect.TableVariableDeclaration</c>/
		/// <c>ScalarVariableDeclaration</c>, the positional body boundary, and a full emitted
		/// context, all without touching the still-stubbed <c>ShredStatement</c>/<c>ShredParamName</c>
		/// path (which only INPUT tables/objects need).
		/// </summary>
		[Fact]
		public System.Threading.Tasks.Task OutputOnlyQueryGeneratesFullContext()
		{
			var name = nameof(OutputOnlyQueryGeneratesFullContext);
			return TestHelper.VerifyPostgres([TestHelper.TestHeaderPostgres([name])], [$$"""
				--Name: {{name}}
				Create Temp Table Returns_Person (PersonID int Primary Key, Name text Not Null, Balance numeric);
				Create Temp Table Return_Total (Total int);
				Insert Into Returns_Person (PersonID, Name, Balance) Select 1, 'Ada', 100.00;
				Insert Into Return_Total (Total) Select Count(*) From Returns_Person;
				Select PersonID, Name, Balance From Returns_Person;
				Select Total From Return_Total;
				"""]);
		}
	}
}
