namespace SQuiL.Dialects;

/// <summary>
/// Marker for dialects whose header is a sequence of <c>Create Temp Table</c> statements (SQLite,
/// PostgreSQL) rather than T-SQL <c>Declare @var</c> + <c>Use</c>. The tokenizer/parser recognition
/// of the temp-table header, positional body boundary, and sample-DML stripping is identical for
/// every such dialect; only the emitted leaf strings (types, shred, casing) differ per dialect.
/// </summary>
public interface ITempTableHeaderDialect : ISqlDialect
{
	/// <summary>
	/// The statement that fills a scalar input's one-column temp table from its bound command
	/// parameter, for example <c>Insert Into Param_Lang (Lang) Values (@Param_Lang);</c>. The body
	/// reads the scalar from that table (<c>(Select Lang From Param_Lang)</c>), so the table must
	/// exist and hold the request's value.
	/// </summary>
	string ScalarInputInsert(string tableName, SQuiL.SourceGenerator.Parser.CodeItem column, string parameterName);
}
