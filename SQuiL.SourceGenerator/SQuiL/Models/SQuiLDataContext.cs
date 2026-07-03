using Microsoft.CodeAnalysis;
using Microsoft.Data.SqlClient;

using SQuiL.Generator;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.CodeDom.Compiler;
using System.Text;

namespace SQuiL.Models;

/// <summary>
/// Generates the data-context partial class for a single SQL query file.  The emitted class
/// contains one <c>async Task&lt;…&gt;</c> method that builds the SQL command, sends parameters,
/// executes the query, and maps result sets back to the response model.
/// </summary>
/// <param name="NameSpace">The C# namespace of the data-context class.</param>
/// <param name="ClassName">The data-context class name.</param>
/// <param name="Method">The query method name (SQL file name without extension).</param>
/// <param name="Setting">The connection-string configuration key passed to <c>ConnectionStringBuilder</c>.</param>
/// <param name="Blocks">All parsed code blocks from the SQL file (USE, DECLARE, BODY).</param>
public class SQuiLDataContext(
		string NameSpace,
		string ClassName,
		string Method,
		string Setting,
		List<CodeBlock> Blocks,
		bool Enabled = false,
		bool DebugRollback = true)
{
	/// <summary>
	/// Generates the full C# source for the data-context partial class.
	/// </summary>
	/// <param name="generation">The aggregated models (request, response, tables) for this query.</param>
	/// <returns>The generated source text, or an exception if a required SQL block is missing.</returns>
	public ExceptionOrValue<string> GenerateCode(SQuiLFileGeneration generation)
	{
		StringBuilder builder = new();
		StringWriter text = new(builder);
		IndentedTextWriter writer = new(text, "\t");

		var database = Blocks.FirstOrDefault(p => p.CodeType == CodeType.USING)?.Name;
		if (database is null)
			return new Exception("Missing USE statement.");

		var query = Blocks.First(p => p.CodeType == CodeType.BODY)?.Name;
		if (query is null)
			return new Exception("Missing query body.");

		var inputs = Blocks
			.Where(p => (p.CodeType & CodeType.INPUT) == CodeType.INPUT
				&& p.CodeType != CodeType.INPUT_ARGUMENT)
			.Select(p => (CodeBlock: p, Query: TableDeclaration(p)))
			.ToList();

		var outputs = Blocks
			.Where(p => (p.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT)
			.Select(p => (CodeBlock: p, Query: p.CodeType == CodeType.OUTPUT_VARIABLE
				? $"Declare {p.DatabaseType.Original};"
				: TableDeclaration(p)))
			.ToList();

		writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			using Microsoft.Data.SqlClient;

			using System;
			using System.Collections.Generic;
			using System.Data.Common;
			using System.Threading;
			using System.Threading.Tasks;

			using {{SourceGeneratorHelper.NamespaceName}};

			namespace {{NameSpace}};

			""");

		Process();
		return new(text.ToString());

		void Process()
		{
			writer.Block($$"""partial class {{ClassName}} : {{SourceGeneratorHelper.BaseDataContextClassName}}""", () =>
			{
				var errorReturnType = true;
				var returnType = generation.Response.ModelName;
				var noResponse = outputs.Count() == 0;

				if (noResponse)
					errorReturnType = false;
				//else
				returnType = noResponse
					? SourceGeneratorHelper.ResultTypeAttributeName
					: $"{SourceGeneratorHelper.ResultTypeAttributeName}<{returnType}>";

				writer.Block($$"""
								public async Task<{{returnType}}> Process{{Method}}Async(
									{{generation.Request.ModelName}} request,
									CancellationToken cancellationToken = default!)
								""", () =>
				{
					writer.Block($$"""
									var builder = ConnectionStringBuilder("{{Setting}}");
									using var connection = CreateConnection(builder.ConnectionString);

									var command = connection.CreateCommand();

									""");

					// ── Compute debug-hoist state before emitting parameters ──────
					// `inputArgs` is also used inside CommandParameters(); capture it here
					// so both sites agree on the same list.
					var inputArgs = Blocks
						.Where(p => p.CodeType == CodeType.INPUT_ARGUMENT)
						.ToList();
					bool HasSpecialPre(string name) => inputArgs.Any(p => p.IsSpecialDeclaration && p.Name == name);
					var hasDebug = HasSpecialPre(SQuiLGenerator.Debug);
					var hasSuppressDebug = HasSpecialPre(SQuiLGenerator.SuppressDebug);

					// hoistDebug is true only for transaction contexts that also declare @Debug.
					// Non-transaction queries MUST NOT hoist — their snapshots must stay unchanged.
					var hoistDebug = Enabled && hasDebug;

					// The debug expression reused from CommandParameters (must stay in sync with
					// @SuppressDebug variant so the two sites are always identical).
					var debugExpr = hasSuppressDebug
						? $$"""!request.SuppressDebug && (request.Debug || {{SQuiLGenerator.EnvironmentName}} != "Production")"""
						: $"request.Debug || {SQuiLGenerator.EnvironmentName} != \"Production\"";

					// Commit gate (reader path): uses the `errors` list that the reader path declares.
					// Gated on !__debug when debugRollback is on AND @Debug was declared.
					var readerCommitGate = (Enabled && hasDebug && DebugRollback)
						? "errors.Count == 0 && !__debug"
						: "errors.Count == 0";

					// Commit gate (non-query path): no `errors` list exists on this path — "success"
					// is implicitly errors.Count == 0 (we're inside the try block after ExecuteNonQuery).
					// Gate only on !__debug when debugRollback applies.
					var nonQueryCommitGate = (Enabled && hasDebug && DebugRollback)
						? "!__debug"
						: null; // null = unconditional commit (original behaviour)

					// Emit hoist local BEFORE the parameters list (transaction + @Debug only).
					if (hoistDebug)
					{
						writer.WriteLine($"var __debug = {debugExpr};");
						writer.WriteLine();
					}

					CommandParameters(inputArgs, hasDebug, hasSuppressDebug, hoistDebug);
					writer.Block($"""

									command.CommandText = Query(parameters);
									command.Parameters.AddRange(parameters.ToArray());

									await connection.OpenAsync(cancellationToken);

									""");
					if (Enabled)
						writer.Block("""
							using var transaction = connection.BeginTransaction();
							command.Transaction = transaction;

							""");
					if (!errorReturnType)
					{
						writer.Block("try", () =>
						{
							writer.WriteLine("await command.ExecuteNonQueryAsync(cancellationToken);");
							if (Enabled)
							{
								if (nonQueryCommitGate is not null)
								{
									writer.WriteLine($"if ({nonQueryCommitGate})");
									writer.Indent++; writer.WriteLine("transaction.Commit();"); writer.Indent--;
									writer.WriteLine("else");
									writer.Indent++; writer.WriteLine("transaction.Rollback();"); writer.Indent--;
								}
								else
								{
									writer.WriteLine("transaction.Commit();");
								}
							}
							if (noResponse)
								writer.WriteLine($"return {returnType}.Success;");
							else
								WriteReturn(generation.Response.ModelName, "");
						});
						writer.Block("catch(Microsoft.Data.SqlClient.SqlException e)", () =>
						{
							if (Enabled) writer.WriteLine("transaction.Rollback();");
							WriteReturn("SQuiLError", "e");
						});

						void WriteReturn(string model, string parameter)
						{
							writer.WriteLine($"""return new {returnType}(new {model}({parameter}));""");
						}
					}
					else
					{
						writer.WriteLine($"{generation.Response.ModelName} response = new();");
						VariableSetTracking();
						writer.WriteLine();
						writer.WriteLine($"List<SQuiLError> errors = [];");
						writer.Block("try", () =>
						{
							writer.Block("""
							using var reader = await command.ExecuteReaderAsync(cancellationToken);

							do
							""",
									new Action(() =>
									{
										writer.WriteLine("var __shape = ShapeKey(reader);");
										writer.Block("switch (__shape)", SwitchStatements);
									}),
									"""
							while (await reader.NextResultAsync(cancellationToken));
							""");
						}, new IndentedTextWriterBlock("catch(SqlException e)", () =>
						{
							writer.WriteLine("errors.Add(new(e.Number, 11, e.State, e.LineNumber, e.Procedure, e.Message));");
						}));

						writer.WriteLine();

						if (outputs.Count > 0)
						{
							// Deduplicate by name: after the suffix drop, @Returns_X and @Return_X both
							// have block.Name == "X"; emit the error check only once per unique name.
							var errorSeen = new HashSet<string>();
							foreach (var block in outputs.Select(p => p.CodeBlock).OrderBy(p => p.IsObject ? 1 : p.IsTable ? 2 : 0))
							{
								if (!errorSeen.Add(block.Name)) continue;
								WriteAddError(block);
							}

							writer.WriteLine();

							void WriteAddError(CodeBlock block)
							{
								var type = block.IsObject ? "object" : block.IsTable ? "table" : "scaler";
								var message = $"Expected return {type} `{block.Name}`";
								var line = DiagnosticsMessages.Newline.Matches(builder.ToString()).Count + 1;

								writer.Write($"""if (!is{block.Name}) """);
								writer.WriteLine($"""errors.Add(new(51001, 12, 1, {line}, "{block.Name}", "{message}"));""");
							}
						}

						if (Enabled)
						{
							writer.WriteLine($"if ({readerCommitGate})");
							writer.Indent++; writer.WriteLine("transaction.Commit();"); writer.Indent--;
							writer.WriteLine("else");
							writer.Indent++; writer.WriteLine("transaction.Rollback();"); writer.Indent--;
							writer.WriteLine();
						}

						writer.WriteLine("if(errors.Count == 0)");
						writer.Indent++;
						if (noResponse)
							writer.WriteLine($"return {returnType}.Success;");
						else
							writer.WriteLine("return new(response);");
						writer.Indent--;
						writer.WriteLine();
						writer.WriteLine("return new(errors);");
					}
					InsertQueries();
					writer.Block($""""
				
									string Query(List<DbParameter> parameters) => $"""
									"""");
					QueryDeclareStatements();
					writer.Block($$""""
									Use [{builder.InitialCatalog}];
							
									{{query}}
									""";
									"""");
				});
			});
		}

		void QueryDeclareStatements()
		{
			foreach (var (CodeBlock, Query) in inputs)
				writer.Block($$"""
					{{Query}}
					{input{{CodeBlock.Name}}(parameters)}

					""");

			foreach (var output in outputs)
			{
				writer.Block(output.Query);
				writer.WriteLine();
			}
		}

		void VariableSetTracking()
		{
			if (outputs.Count == 0)
				return;

			writer.WriteLine();

			// Deduplicate by name: after the suffix drop, @Returns_X and @Return_X both
			// have block.Name == "X"; emit the tracking variable only once.
			var seen = new HashSet<string>();
			foreach (var (block, query) in outputs
				.OrderBy(p => p.CodeBlock.IsObject ? 1 : p.CodeBlock.IsTable ? 2 : 0))
			{
				if (!seen.Add(block.Name)) continue;
				writer.WriteLine($"var is{block.Name} = false;");
			}
		}

		void SwitchStatements()
		{
			try
			{
				// Deduplicate by name: after the suffix drop, @Returns_X and @Return_X both
				// resolve to the same Response property; only emit the first switch case per name
				// (the table/list variant wins when declared before the object variant).
				var switchSeen = new HashSet<string>();
				foreach (var (block, query) in outputs)
				{
					if (!switchSeen.Add(block.Name)) continue;

					// Result sets are routed by their column signature (shape key) instead of an
					// injected sentinel column. A scalar's key is its single aliased column
					// (name = the scalar's base name); a table/object's key is its ordered columns.
					var shapeKey = (block.IsTable || block.IsObject)
						? SQuiLShapeKey.ShapeKeyOf(block)
						: SQuiLShapeKey.ScalarKeyOf(block.Name, block.CSharpType(block.Name));

					writer.Block($"""case "{shapeKey}":""", () =>
					{
						if (!block.IsTable)
						{
							writer.WriteLine($"if (is{block.Name}) throw new Exception(");
							writer.Indent++;
							writer.WriteLine($"\"Already returned value for `{block.Name}`\");");
							writer.Indent--;
							writer.WriteLine();
						}
						writer.WriteLine($"is{block.Name} = true;");
						writer.WriteLine();
						if (block.IsTable)
							writer.WriteLine($"response.{block.Name} ??= [];");
						writer.WriteLine("if (!await reader.ReadAsync(cancellationToken)) break;");
						if (block.IsTable)
						{
							LoopProperties($"response.{block.Name}", block.Properties);
						}
						else if (block.IsObject)
						{
							writer.Block($"""

								if (response.{block.Name} is not null)
									throw new Exception("{block.Name} was already set.");

								""");
							writer.Write($"response.{block.Name} = new(");
							writer.Indent++;
							var comma = "";
							foreach (var item in block.Properties)
							{
								writer.WriteLine(comma);
								if (item.IsNullable)
									writer.Write($"""reader.IsDBNull(reader.GetOrdinal("{item.Identifier.Value}")) ? default! : """);
								writer.Write($"""{item.DataReader()}(reader.GetOrdinal("{item.Identifier.Value}"))""");
								comma = ",";
							}
							writer.Indent--;
							writer.WriteLine(");");
							writer.Block($"""

								if (await reader.ReadAsync(cancellationToken))
									throw new Exception(
										"Return object results in more than one object. Consider using a return table instead.");

								""");
						}
						else
						{
							writer.WriteLine();

							var isNullable = "null";
							if (!block.IsNullable)
							{
								var exception = $"Return value for {block.Name} cannot be null.";
								isNullable = $"""throw new NullReferenceException("{exception}")""";
							}

							writer.WriteLine($"response.{block.Name} = !reader.IsDBNull(0) ? {block.DataReader()}(0) : {isNullable};");
						}
						writer.WriteLine("break;");
					});
				}
			}
			finally
			{
				//writer.WriteLine("""//default: throw new Exception($"Invalid Table `{reader.GetString(0)}`");""");
			}

			void LoopProperties(string model, List<CodeItem> properties)
			{
				writer.WriteLine();

				foreach (var item in properties)
					writer.WriteLine($"""var index{item.Identifier.Value} = reader.GetOrdinal("{item.Identifier.Value}");""");

				writer.WriteLine();
				writer.Block("do", () =>
				{
					foreach (var item in properties)
					{
						var defaultCondition = "";
						if (item.IsNullable)
							defaultCondition = $"reader.IsDBNull(index{item.Identifier.Value}) ? default! : ";
						writer.WriteLine($"""var value{item.Identifier.Value} = {defaultCondition}{item.DataReader()}(index{item.Identifier.Value});""");
					}

					writer.WriteLine();
					var positional = properties.Where(p => p.DefaultValue is null).ToList();
					var defaulted = properties.Where(p => p.DefaultValue is not null).ToList();
					writer.Write($"{model}.Add(new(");
					writer.Indent++;
					var comma = "";
					foreach (var item in positional)
					{
						writer.WriteLine(comma);
						writer.Write($"value{item.Identifier.Value}");
						comma = ",";
					}
					writer.Indent--;
					if (defaulted.Count == 0)
					{
						writer.WriteLine("));");
					}
					else
					{
						writer.WriteLine(")");
						writer.WriteLine("{");
						writer.Indent++;
						foreach (var item in defaulted)
							writer.WriteLine($"{item.Identifier.Value} = value{item.Identifier.Value},");
						writer.Indent--;
						writer.WriteLine("});");
					}
				}, $"""while (await reader.ReadAsync(cancellationToken));""");
			}
		}

		void InsertQueries()
		{
			foreach (var (CodeBlock, Query) in inputs)
			{
				writer.WriteLine();
				writer.Block($"string input{CodeBlock.Name}(List<DbParameter> parameters)", () =>
				{
					if (CodeBlock.IsTable)
					{
						writer.WriteLine($"""if (request.{CodeBlock.Name} is null || request.{CodeBlock.Name}.Count == 0) return "";""");
						writer.WriteLine();

						// Per-row string-length guards (preserves the old AddParams throw).
						// Track a row index so an over-length value reports which element failed.
						if (CodeBlock.Properties.Any(IsSizedString))
						{
							writer.WriteLine("var index = 0;");
							writer.Block($"foreach (var item in request.{CodeBlock.Name})", () =>
							{
								EmitStringLengthGuards(CodeBlock, "item", "index");
								writer.WriteLine("index++;");
							});
							writer.WriteLine();
						}

						writer.WriteLine($"""AddJsonParameter(parameters, "{SQuiLShred.JsonParamName(CodeBlock)}", request.{CodeBlock.Name});""");
					}
					else if (CodeBlock.IsObject)
					{
						writer.Block($"""
							if (request.{CodeBlock.Name} is null)
								throw new NullReferenceException(
									"{generation.Request.ModelName} is missing the required property {CodeBlock.Name}.");

							""");

						EmitStringLengthGuards(CodeBlock, $"request.{CodeBlock.Name}");
						if (CodeBlock.Properties.Any(IsSizedString)) writer.WriteLine();

						writer.WriteLine($$"""AddJsonParameter(parameters, "{{SQuiLShred.JsonParamName(CodeBlock)}}", new[] { request.{{CodeBlock.Name}} });""");
					}

					// Emit `return """ <shred sql> """;` through Block — exactly how the
					// outer Query(...) literal is already emitted. Block applies ONE uniform
					// base indent to every line it writes, so the content lines and the
					// closing """ always land in the same column and the raw literal strips
					// cleanly. A PLAIN """ (not $"""): the shred SQL is fully static now
					// (all row data lives in the JSON parameter), nothing to interpolate.
					writer.WriteLine();
					writer.Block("return \"\"\"");
					writer.Block(SQuiLShred.ShredSql(CodeBlock));
					writer.Block("\"\"\";");
				});
			}

			// Whether a property is a sized (non-max) string column that needs a length guard.
			static bool IsSizedString(CodeItem p)
				=> p.Type.Type == TokenType.TYPE_STRING
					&& p.Type.Value is { } s
					&& !s.Equals("max", StringComparison.OrdinalIgnoreCase);

			// Emits a throw for any sized varchar/nvarchar/char column whose value exceeds its
			// declared length, BEFORE serialization (do not rely on silent OPENJSON truncation).
			// `itemExpr` is the per-row item expression: for a list, called inside a foreach
			// over `request.<Name>` with item "item"; for an object, "request.<Name>".
			// `indexExpr` is the runtime loop-counter variable for a list (so the message names
			// the failing row, e.g. `Rows[3]`); null for a single object (no index).
			void EmitStringLengthGuards(CodeBlock block, string itemExpr, string? indexExpr = null)
			{
				// Row locator embedded into the runtime message: "[{index}]" for a list
				// element, empty for a single object. The inner single braces survive as a
				// runtime interpolation hole in the emitted $"..." string.
				var locator = indexExpr is null ? "" : $"[{{{indexExpr}}}]";

				foreach (var p in block.Properties)
				{
					if (p.Type.Type != TokenType.TYPE_STRING) continue;
					var size = p.Type.Value;
					if (size is null || size.Equals("max", StringComparison.OrdinalIgnoreCase)) continue;

					var value = $"{itemExpr}.{p.Identifier.Value}";
					writer.Block($"if ({value} is not null && {value}.Length > {size})", () =>
					{
						writer.WriteLine($$"""
							throw new Exception($"{{generation.Request.ModelName}} {{block.Name}}{{locator}}.{{p.Identifier.Value}} exceeds its maximum length of {{size}} characters.");
							""");
					});
				}
			}
		}

		string TableDeclaration(CodeBlock block)
		{
			return $"""
				Declare {block.DatabaseType.Original}(
					{string.Join($",{writer.NewLine}\t", block.Properties.Select(p
						=> $"[{p.Identifier.Value}] {p.Type.Original}{(p.IsNullable ? " Null" : "")}"))});
				""";
		}

		void CommandParameters(
			List<CodeBlock>? precomputedInputArgs = null,
			bool preHasDebug = false,
			bool preHasSuppressDebug = false,
			bool hoistDebug = false)
		{
			writer.WriteLine("List<DbParameter> parameters = new()");
			writer.WriteLine("{");
			writer.Indent++;

			// Use the pre-computed inputArgs list when provided (avoids double-enumeration
			// and ensures the hoist site and parameter site agree on the same blocks).
			var inputArgs = precomputedInputArgs ?? Blocks
				.Where(p => p.CodeType == CodeType.INPUT_ARGUMENT)
				.ToList();

			bool HasSpecial(string name) => inputArgs.Any(p => p.IsSpecialDeclaration && p.Name == name);
			// When pre-computed values are supplied (transaction path), use them directly
			// so the hoist and the parameter share the same computed state.
			var hasDebug = precomputedInputArgs is not null ? preHasDebug : HasSpecial(SQuiLGenerator.Debug);
			var hasSuppressDebug = precomputedInputArgs is not null ? preHasSuppressDebug : HasSpecial(SQuiLGenerator.SuppressDebug);
			var hasEnvironmentName = HasSpecial(SQuiLGenerator.EnvironmentName);
			var asOfDate = inputArgs.FirstOrDefault(p => p.IsSpecialDeclaration && p.Name == SQuiLGenerator.AsOfDate);

			var notFirst = false;
			void Comma()
			{
				if (notFirst) writer.WriteLine(",");
				notFirst = true;
			}

			// All input specials are opt-in: each command parameter is emitted only when the
			// corresponding bare special is declared in the header. Every emitted parameter
			// shares the same `Comma()` separator discipline (no leading/trailing comma).
			// Order: EnvironmentName, Debug, SuppressDebug, AsOfDate, then the regular params.
			if (hasEnvironmentName)
			{
				Comma();
				writer.Write($$"""CreateParameter("@{{SQuiLGenerator.EnvironmentName}}", System.Data.SqlDbType.VarChar, {{SQuiLGenerator.EnvironmentName}}.Length, {{SQuiLGenerator.EnvironmentName}})""");
			}

			if (hasDebug)
			{
				// When the debug expression is hoisted to a local (transaction + @Debug),
				// the parameter references the hoisted local `__debug` instead of the inline
				// expression. Non-transaction @Debug queries keep the inline form unchanged.
				var debugValue = hoistDebug
					? "__debug"
					: (hasSuppressDebug
						? $$"""!request.SuppressDebug && (request.Debug || {{SQuiLGenerator.EnvironmentName}} != "Production")"""
						: $"request.Debug || {SQuiLGenerator.EnvironmentName} != \"Production\"");
				Comma();
				writer.Write($$"""CreateParameter("@{{SQuiLGenerator.Debug}}", System.Data.SqlDbType.Bit, {{debugValue}})""");
			}

			if (hasSuppressDebug)
			{
				Comma();
				writer.Write($$"""CreateParameter("@{{SQuiLGenerator.SuppressDebug}}", System.Data.SqlDbType.Bit, request.SuppressDebug)""");
			}

			if (asOfDate is not null)
			{
				Comma();
				writer.Write($$"""CreateParameter("@{{SQuiLGenerator.AsOfDate}}", {{asOfDate.SqlDbType()}}, request.AsOfDate ?? {{AsOfDateNowExpression(asOfDate)}})""");
			}

			foreach (var parameter in inputArgs)
			{
				// Skip bare specials (handled above / in Task 6). @Param_AsOfDate
				// (IsSpecialDeclaration == false) emits normally through this loop.
				if (parameter.IsSpecialDeclaration)
					continue;

				Comma();
				writer.Write($$"""CreateParameter("@Param_{{parameter.Name}}", {{parameter.SqlDbType()}}, """);

				WriteValue();

				if (parameter.IsNullable)
					writer.Write($$""", p => p.IsNullable = true""");

				writer.Write(")");

				void WriteValue()
				{
					var value = $"request.{parameter.Name}";

					if (parameter.DatabaseType.Type != TokenType.TYPE_STRING
						|| parameter.Size?.Equals("max", StringComparison.OrdinalIgnoreCase) == true)
					{
						writer.Write(value);

						if (parameter.IsNullable)
							writer.WriteLine($" ?? (object)System.DBNull.Value");

						return;
					}

					writer.WriteLine($"{value} switch");
					writer.WriteLine("{");
					writer.Indent++;
					writer.WriteLine("null => (object)System.DBNull.Value,");
					writer.WriteLine($$"""{ Length: <= {{parameter.Size}} } => {{value}},""");
					writer.WriteLine("_ => throw new Exception(");
					writer.Indent++;
					writer.WriteLine($""" "Request model data is larger then database size for the property [{parameter.Name}].")"""[1..]);
					writer.Indent -= 2;
					writer.Write("}");
				}
			}

			if (notFirst)
				writer.WriteLine();
			writer.Indent--;
			writer.WriteLine("};");

			// The C# expression substituted for a null @AsOfDate. `.Now` is not uniform across
			// the date/time CLR types the type map can produce, so switch on the SAME mapped
			// CLR type Task 5 used for the Request property (`block.CSharpType("")`):
			//   System.DateOnly       -> System.DateOnly.FromDateTime(System.DateTime.Now)  (DateOnly has no .Now)
			//   System.DateTime       -> System.DateTime.Now
			//   System.DateTimeOffset -> System.DateTimeOffset.Now
			static string AsOfDateNowExpression(CodeBlock block) => block.CSharpType("") switch
			{
				"System.DateOnly" => "System.DateOnly.FromDateTime(System.DateTime.Now)",
				"System.DateTimeOffset" => "System.DateTimeOffset.Now",
				_ => "System.DateTime.Now",
			};
		}
		/*
		string F(IEnumerable<string> lines)
			=> string.Join($"{newline}{newline}{tabs}", lines);
		*/
	}
}

/// <summary>
/// Dialect-specific helpers for the JSON/OPENJSON param-sharding feature (TODO #1).
/// Generates the SQL-Server OPENJSON shred statement for a table or object input block.
/// A future <c>ISqlDialect</c> seam (TODO #6) will substitute the dialect-appropriate
/// equivalent (e.g. <c>json_to_recordset</c> for PostgreSQL).
/// </summary>
public static class SQuiLShred
{
	/// <summary>
	/// Returns the JSON parameter name for the given input block:
	/// <c>@__json_Params_&lt;Name&gt;</c> for a table, <c>@__json_Param_&lt;Name&gt;</c> for an object.
	/// </summary>
	public static string JsonParamName(CodeBlock block)
		=> $"@__json_Param{(block.IsTable ? "s" : "")}_{block.Name}";

	/// <summary>
	/// Builds the full <c>Insert Into … Select … From OpenJson(…) With (…);</c> shred statement
	/// for the given input block. Binary columns are captured as <c>nvarchar(max)</c> in the
	/// WITH clause and converted with <c>CONVERT(varbinary(N), col, 2)</c> in the SELECT.
	/// </summary>
	public static string ShredSql(CodeBlock block)
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
			From OpenJson({JsonParamName(block)})
			With (
				{withList});
			""".Replace("\r\n", "\n");

		static string SelectColumn(CodeItem p)
			=> IsBinary(p)
				? $"Convert(varbinary({BinarySize(p)}), [{p.Identifier.Value}], 2)"
				: $"[{p.Identifier.Value}]";

		static string WithColumn(CodeItem p)
		{
			var path = $"'$.{p.Identifier.Value}'";
			return IsBinary(p)
				? $"[{p.Identifier.Value}] nvarchar(max) {path}"
				: $"[{p.Identifier.Value}] {p.Type.Original} {path}";
		}

		static bool IsBinary(CodeItem p)
			=> p.Type.Type is TokenType.TYPE_BINARY or TokenType.TYPE_VARBINARY;

		static string BinarySize(CodeItem p)
			=> p.Type.Value is null || p.Type.Value.Equals("max", StringComparison.OrdinalIgnoreCase)
				? "max"
				: p.Type.Value;
	}
}
