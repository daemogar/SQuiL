namespace SQuiL.Dialects;

/// <summary>
/// Marker for dialects whose header is a sequence of <c>Create Temp Table</c> statements (SQLite,
/// PostgreSQL) rather than T-SQL <c>Declare @var</c> + <c>Use</c>. The tokenizer/parser recognition
/// of the temp-table header, positional body boundary, and sample-DML stripping is identical for
/// every such dialect; only the emitted leaf strings (types, shred, casing) differ per dialect.
/// </summary>
public interface ITempTableHeaderDialect : ISqlDialect { }
