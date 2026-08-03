using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Intermediate representation of one parsed SQL <c>DECLARE</c>, <c>USE</c>, or <c>BODY</c>
/// statement.  Produced by <see cref="SQuiLParser"/> and consumed by the model builders and
/// code generators.
/// </summary>
/// <param name="CodeType">The classified role of this block (input, output, using, body, etc.).</param>
/// <param name="DatabaseType">The SQL type token that describes the block's data type.</param>
/// <param name="Name">The variable base name (without the <c>@Param_</c> / <c>@Return_</c> prefix).</param>
/// <param name="DefaultValue">The literal default value string, if any, from the DECLARE statement.</param>
public record CodeBlock(CodeType CodeType, Token DatabaseType, string Name, string? DefaultValue)
{
	/// <summary><c>true</c> when the variable represents a single-row structured type (<c>table</c> declared as object).</summary>
	public bool IsObject { get; }

	/// <summary><c>true</c> when the variable represents a multi-row table-valued type.</summary>
	public bool IsTable { get; }

	/// <summary><c>true</c> when the variable is a binary or varbinary type.</summary>
	public bool IsBinary { get; }

	/// <summary><c>true</c> when the variable has no default value and must be supplied by the caller.</summary>
	public bool IsRequired { get; }

	/// <summary>Explicit nullability marker on the declare: true=<c>null</c>, false=<c>not null</c>, null=unmarked.</summary>
	public bool? IsNullableMarker { get; init; }

	/// <summary>
	/// <c>true</c> when a scalar declare carried a standalone <c>null</c>/<c>not null</c> marker
	/// (rather than an <c>= null</c> initializer). Standalone markers are invalid T-SQL on a
	/// scalar Declare — SP0037 flags them at build time. Never set for the <c>= null</c> path.
	/// </summary>
	public bool HasScalarNullabilityMarker { get; init; }

	/// <summary><c>true</c> when the generated C# property should be nullable —
	/// non-nullable UNLESS an explicit <c>null</c> marker is present (unified rule).</summary>
	public bool IsNullable => IsNullableMarker == true;

	/// <summary>The string size or precision extracted from the type token (e.g. <c>"50"</c> for <c>varchar(50)</c>).</summary>
	public string? Size { get; set; }

	/// <summary>
	/// <c>true</c> when this block was declared as a bare special variable
	/// (<c>@Debug</c>, <c>@SuppressDebug</c>, <c>@EnvironmentName</c>, or <c>@AsOfDate</c>) — i.e. WITHOUT a
	/// <c>@Param_</c>/<c>@Return_</c> prefix. Distinguishes the bare special from an ordinary param whose
	/// stripped name collides (e.g. <c>@Param_AsOfDate</c>), since both otherwise produce a block named "AsOfDate".
	/// </summary>
	public bool IsSpecialDeclaration { get; init; }

	/// <summary>Columns or sub-properties for table and object variable types; <c>null</c> for scalars.</summary>
	public List<CodeItem> Properties { get; } = default!;

	/// <summary>
	/// For a table/object block declared via a temp-table-header dialect (SQLite/PostgreSQL)'s
	/// <c>Create Temp Table</c>: the ORIGINAL (unstripped) physical table name — e.g.
	/// <c>"Returns_Person"</c> for a block whose <see cref="Name"/> is the stripped C#-model name
	/// <c>"Person"</c>. These dialects' temp tables are referenced verbatim by their full name in
	/// the (author-written, never-rewritten) body, so
	/// <see cref="SQuiL.Dialects.SqliteDialect.TableVariableDeclaration"/> must recreate the table
	/// under that exact name — the stripped <see cref="Name"/> would not match. <c>null</c> for
	/// SQL Server blocks (whose full name is carried by <see cref="DatabaseType"/>'s <c>Original</c>).
	/// </summary>
	/// <remarks>
	/// <c>internal</c> (not public) deliberately: a record's synthesized <c>ToString</c>/
	/// <c>PrintMembers</c> prints only PUBLIC members, and several diagnostic-message snapshots
	/// dump the full <c>CodeBlock.ToString()</c>. Keeping these three temp-table-header-dialect-only
	/// members internal keeps that dump — and every existing SQL Server <c>.verified.*</c> snapshot
	/// — byte-identical.
	/// </remarks>
	internal string? TempTableName { get; init; }

	/// <summary>
	/// For a scalar block collapsed from a single-column <c>Create Temp Table</c> declaration
	/// under a temp-table-header dialect (SQLite/PostgreSQL) (see <c>SQuiLParser</c>'s collapse
	/// branch): the ORIGINAL (unstripped) table name, e.g. <c>"Return_Total"</c> for a block whose
	/// <see cref="Name"/> is the stripped <c>"Total"</c>. These dialects have no bare
	/// scalar-declare syntax, so
	/// <see cref="SQuiL.Dialects.SqliteDialect.ScalarVariableDeclaration"/> needs this to
	/// regenerate a physically-matching <c>Create Temp Table</c> statement. <c>null</c> for
	/// every other block (SQL Server scalars, and every table/object block, which already carry
	/// their own full name via <see cref="DatabaseType"/>'s <c>Original</c>).
	/// </summary>
	internal string? TempScalarTableName { get; init; }

	/// <summary>
	/// For a scalar block collapsed from a single-column <c>Create Temp Table</c> declaration
	/// under a temp-table-header dialect (SQLite/PostgreSQL): the original single column
	/// (identifier, type, nullability, default, primary-key marker) that the collapse would
	/// otherwise discard. Paired with <see cref="TempScalarTableName"/>; <c>null</c> for every
	/// other block.
	/// </summary>
	internal CodeItem? TempScalarColumn { get; init; }

	/// <summary>
	/// Convenience constructor that derives the block name and default value from the type token directly.
	/// </summary>
	public CodeBlock(CodeType CodeType, Token Token)
	: this(CodeType, Token, Token.Value, default)
	{
		if (Token.Type == TokenType.TYPE_TABLE)
		{
			IsTable = true;
			Properties = [];
		}
		else if (Token.Type == TokenType.TYPE_OBJECT)
		{
			IsObject = true;
			Properties = [];
		}
		else if (Token.Type == TokenType.TYPE_BINARY || Token.Type == TokenType.TYPE_VARBINARY || Token.Type == TokenType.TYPE_IMAGE || Token.Type == TokenType.TYPE_TIMESTAMP)
		{
			IsBinary = true;
			IsRequired = true;
		}
		else
		{
			IsRequired = true;
		}
	}

	/// <summary>Returns a human-readable summary of this block (delegates to record <c>ToString</c>).</summary>
	public string Source() => ToString();

	/// <summary>Returns the <c>System.Data.SqlDbType.*</c> expression for this block's type, including size.</summary>
	public string SqlDbType() => DatabaseType.SqlDbType(Size);

	/// <summary>Returns the <c>reader.GetXxx</c> method fragment appropriate for this block's SQL type.</summary>
	public string DataReader() => DatabaseType.DataReader();

	/// <summary>Returns the C# type string, using <paramref name="tablename"/> for object/table types.</summary>
	public string CSharpType(string tablename)
	=> DatabaseType.CSharpType(() => tablename);

	/// <summary>Legacy helper that derives a table type name from the model name and block name.</summary>
	public string CSharpType_Deprecated(string modelname)
		=> DatabaseType.CSharpType(() => $"{modelname}{Name}Table");

	/// <summary>Returns the C# default-value expression for this block, or <c>null</c> if none.</summary>
	public string? CSharpValue() => DatabaseType.CSharpValue(DefaultValue);
}
