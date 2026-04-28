using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

/// <summary>
/// Represents one column inside a SQL table-variable declaration (e.g. a column
/// in <c>@Params_Name table([Col1] int, [Col2] varchar(50))</c>).
/// Pairs the column name token with its SQL type token and tracks nullability.
/// </summary>
/// <param name="Identifier">The column name token.</param>
/// <param name="Type">The SQL type token for this column.</param>
public record CodeItem(Token Identifier, Token Type)
{
	/// <summary>A string key that uniquely identifies this column by type and name, used for deduplication.</summary>
	public string UniqueIdentifier() => $"{Type.Type} {Identifier.Value}";

	/// <summary><c>true</c> when the column was declared with <c>Null</c>, making it nullable in C#.</summary>
	public bool IsNullable { get; init; }

	/// <summary>Returns the <c>reader.GetXxx</c> method fragment appropriate for this column's SQL type.</summary>
	public string DataReader() => Type.DataReader();

	/// <summary>Returns the C# type string for this column, optionally appending <c>?</c> for nullable columns.</summary>
	/// <param name="callback">Optional override that returns a custom type name (e.g. for object/table columns).</param>
	public string CSharpType(Func<string>? callback = default) => Type.CSharpType(callback) + (IsNullable ? "?" : "");

	/// <summary>
	/// Builds a delegate that generates a SQL property expression for a column at position
	/// <c>i</c> in a parameterized insert, using <c>item.PropertyName</c> as the source value.
	/// </summary>
	public static Func<CodeItem, int, string> SqlProperty(string classname, string model)
		=> (p, i) => p.Type.SqlProperty(classname, model, i, p.Identifier.Value, $"item.{p.Identifier.Value}");
}
