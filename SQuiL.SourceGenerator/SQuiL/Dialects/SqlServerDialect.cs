using System.Linq;

namespace SQuiL.Dialects;

/// <summary>
/// The SQL Server dialect: the single source of every SQL-Server-specific string the
/// generator bakes into emitted C# and SQL. Phase 1 has one concrete dialect; Phase 2
/// extracts an <c>ISqlDialect</c> interface from this surface and adds SQLite.
/// </summary>
public class SqlServerDialect : ISqlDialect
{
	/// <summary>The provider using-directives emitted into every generated data-context file.</summary>
	public System.Collections.Generic.IEnumerable<string> UsingDirectives() => ["using Microsoft.Data.SqlClient;"];

	/// <summary>The provider exception type caught in the generated execute/read try-blocks.</summary>
	public string ProviderExceptionType() => "SqlException";

	/// <summary>The T-SQL database directive that scopes the query to a catalog.</summary>
	public string DatabaseDirective(string catalog) => $"Use [{catalog}];";

	/// <summary>
	/// The T-SQL table-variable declaration for an input/output table block, e.g.
	/// <c>Declare @Params_X table([Col] int Null, ...);</c>. <paramref name="newLine"/> is the
	/// emitter's active newline so the multi-column layout matches the surrounding output exactly.
	/// </summary>
	public string TableVariableDeclaration(SQuiL.SourceGenerator.Parser.CodeBlock block, string newLine)
		=> $"""
			Declare {block.DatabaseType.Original}(
				{string.Join($",{newLine}\t", block.Properties.Select(p
					=> $"[{p.Identifier.Value}] {p.Type.Original}{(p.IsNullable ? " Null" : "")}"))});
			""";

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a column (delegates to the type map).</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeItem item) => item.DataReader();

	/// <summary>The <c>reader.GetXxx</c> accessor fragment for a scalar block (delegates to the type map).</summary>
	public string ReaderAccessor(SQuiL.SourceGenerator.Parser.CodeBlock block) => block.DataReader();

	/// <summary>The <c>System.Data.SqlDbType.*</c> parameter-type expression for a block (delegates to the type map).</summary>
	public string ParamTypeExpr(SQuiL.SourceGenerator.Parser.CodeBlock block) => block.SqlDbType();

	/// <summary>The fully-qualified variable-length-string parameter type (used for @EnvironmentName).</summary>
	public string VarCharType() => "System.Data.SqlDbType.VarChar";

	/// <summary>The fully-qualified boolean parameter type (used for @Debug / @SuppressDebug).</summary>
	public string BitType() => "System.Data.SqlDbType.Bit";

	/// <summary>
	/// Returns the JSON parameter name for the given input block:
	/// <c>@__json_Params_&lt;Name&gt;</c> for a table, <c>@__json_Param_&lt;Name&gt;</c> for an object.
	/// </summary>
	public string ShredParamName(SQuiL.SourceGenerator.Parser.CodeBlock block)
		=> $"@__json_Param{(block.IsTable ? "s" : "")}_{block.Name}";

	/// <summary>
	/// Builds the full <c>Insert Into … Select … From OpenJson(…) With (…);</c> shred statement
	/// for the given input block. Binary columns are captured as <c>nvarchar(max)</c> in the
	/// WITH clause and converted with <c>CONVERT(varbinary(N), col, 2)</c> in the SELECT.
	/// </summary>
	public string ShredStatement(SQuiL.SourceGenerator.Parser.CodeBlock block)
	{
		var varName = $"@Param{(block.IsTable ? "s" : "")}_{block.Name}";
		var cols = block.Properties;

		var insertList = string.Join(", ", cols.Select(p => $"[{p.Identifier.Value}]"));
		var selectList = string.Join(", ", cols.Select(SelectColumn));
		var withList = string.Join($",\n\t", cols.Select(WithColumn));

		// Normalize to \n so `writer.Block` (which splits on \n) strips the raw literal
		// cleanly on every platform — the source-file EOL of this raw literal is CRLF on
		// a Windows checkout, which would otherwise leave stray \r inside the emitted SQL.
		return $"""
			Insert Into {varName}({insertList})
			Select {selectList}
			From OpenJson({ShredParamName(block)})
			With (
				{withList});
			""".Replace("\r\n", "\n");

		static string SelectColumn(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> IsBinary(p)
				? $"Convert(varbinary({BinarySize(p)}), [{p.Identifier.Value}], 2)"
				: $"[{p.Identifier.Value}]";

		static string WithColumn(SQuiL.SourceGenerator.Parser.CodeItem p)
		{
			var path = $"'$.{p.Identifier.Value}'";
			return IsBinary(p)
				? $"[{p.Identifier.Value}] nvarchar(max) {path}"
				: $"[{p.Identifier.Value}] {p.Type.Original} {path}";
		}

		static bool IsBinary(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> p.Type.Type is SQuiL.Tokenizer.TokenType.TYPE_BINARY
				or SQuiL.Tokenizer.TokenType.TYPE_VARBINARY
				or SQuiL.Tokenizer.TokenType.TYPE_IMAGE;

		static string BinarySize(SQuiL.SourceGenerator.Parser.CodeItem p)
			=> p.Type.Value is null || p.Type.Value.Equals("max", System.StringComparison.OrdinalIgnoreCase)
				? "max"
				: p.Type.Value;
	}
}
