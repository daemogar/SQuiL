using Microsoft.CodeAnalysis;

using SQuiL.Generator;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.CodeDom.Compiler;
using System.Text;

namespace SQuiL.Models;

public class SQuiLDataContext(
		string NameSpace,
		string ClassName,
		string Method,
		string Setting,
		List<CodeBlock> Blocks)
{
	internal static string SQuiLTableTypeDatabaseTagName => "__SQuiL__Table__Type__";

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
			.Select(p => (CodeBlock: p, Query: TableDeclaration(p.DatabaseType.Original!, p)))
			.ToList();

		var outputs = Blocks
			.Where(p => (p.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT
				&& !SQuiLGenerator.IsError(p.Name))
			.Select(p => (CodeBlock: p, Query: p.CodeType == CodeType.OUTPUT_VARIABLE
				? $"Declare {p.DatabaseType.Original};"
				: TableDeclaration(p.DatabaseType.Original!, p)))
			.ToList();

		var errors = Blocks
			.Where(p => SQuiLGenerator.IsError(p.Name))
			.Select(p => (CodeBlock: p, Query: TableDeclaration(p.DatabaseType.Original!, p)))
			.ToList();

		writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			using Microsoft.Data.SqlClient;
			using System.Data.Common;

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

				if (outputs.Count() == 0 && errors.Count() == 0)
					errorReturnType = false;
				else
					returnType = $"{SourceGeneratorHelper.ResultTypeAttributeName}<{returnType}>";

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
					CommandParameters();
					writer.Block($"""

									command.CommandText = Query(parameters);
									command.Parameters.AddRange(parameters.ToArray());

									await connection.OpenAsync(cancellationToken);

									""");
					if (!errorReturnType)
					{
						writer.WriteLine("await command.ExecuteNonQueryAsync(cancellationToken);");
						writer.WriteLine();
						writer.WriteLine("return new();");
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
										writer.WriteLine("var tableTag = reader.GetName(0);");
										writer.Block($"""if(tableTag.StartsWith("{SQuiLTableTypeDatabaseTagName}"))""", () =>
										{
											writer.Block($"switch (tableTag)", SwitchStatements);
										});
									}),
									"""
							while (await reader.NextResultAsync(cancellationToken));
							""");
						}, new IndentedTextWriterBlock("catch(SqlException e)", () =>
						{
							writer.WriteLine("errors.Add(new(e.Number, 11, e.State, e.LineNumber, e.Procedure, e.Message));");
						}), new IndentedTextWriterBlock(null, writer.WriteLine));

						if (outputs.Count > 0)
						{
							foreach (var block in outputs.Select(p => p.CodeBlock).OrderBy(p => p.IsObject ? 1 : p.IsTable ? 2 : 0))
								WriteAddError(block);

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

						writer.WriteLine("if(errors.Count == 0)");
						writer.Indent++;
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

			foreach (var error in errors)
			{
				writer.Block(error.Query);
				writer.WriteLine();
			}
		}

		void VariableSetTracking()
		{
			if (outputs.Count == 0)
				return;

			writer.WriteLine();

			foreach (var (block, query) in outputs
				.OrderBy(p => p.CodeBlock.IsObject ? 1 : p.CodeBlock.IsTable ? 2 : 0))
			{
				writer.WriteLine($"var is{block.Name} = false;");
			}
		}

		void SwitchStatements()
		{
			writer.Block($"""case "{SQuiLTableTypeDatabaseTagName}Error__":""", () =>
			{
				writer.WriteLine("if (!await reader.ReadAsync(cancellationToken)) break;");
				foreach (var error in errors)
					LoopProperties("Error", "errors", error.CodeBlock.Properties);
				writer.WriteLine();
				writer.WriteLine("break;");
			});

			try
			{
				foreach (var (block, query) in outputs)
				{
					var switchCase = block.Name;
					if ((block.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT)
						switchCase = $"{(block.IsTable ? "Returns" : "Return")}_{switchCase}";

					writer.Block($"""case "{SQuiLTableTypeDatabaseTagName}{switchCase}__":""", () =>
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
						writer.WriteLine("if (!await reader.ReadAsync(cancellationToken)) break;");
						if (block.IsTable)
						{
							LoopProperties(switchCase, $"response.{block.Name}", block.Properties);
						}
						else if (block.IsObject)
						{
							writer.Block($"""

								if (response.{block.Name} is not null)
									throw new Exception("{block.Name} was already set.");

								""");
							//writer.Block("if (await reader.ReadAsync(cancellationToken))"
							writer.Block($"""if (reader.GetString(0) == "{switchCase}")""", () =>
							{
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
							});
							writer.Block("else", () => writer.WriteLine("continue;"));
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
								var exception = $"Return value for {switchCase} cannot be null.";
								isNullable = $"""throw new NullReferenceException("{exception}")""";
							}

							writer.WriteLine($"response.{block.Name} = !reader.IsDBNull(1) ? {block.DataReader()}(1) : {isNullable};");
						}
						writer.WriteLine("break;");
					});
				}
			}
			finally
			{
				//writer.WriteLine("""//default: throw new Exception($"Invalid Table `{reader.GetString(0)}`");""");
			}

			void LoopProperties(string switchCase, string model, List<CodeItem> properties)
			{
				writer.WriteLine();

				foreach (var item in properties)
					writer.WriteLine($"""var index{item.Identifier.Value} = reader.GetOrdinal("{item.Identifier.Value}");""");

				writer.WriteLine();
				writer.Block("do", () =>
				{
					writer.Block($"""if (reader.GetString(0) == "{switchCase}")""", () =>
					{
						foreach (var item in properties)
						{
							var defaultCondition = "";
							if (item.IsNullable)
								defaultCondition = $"reader.IsDBNull(index{item.Identifier.Value}) ? default! : ";
							writer.WriteLine($"""var value{item.Identifier.Value} = {defaultCondition}{item.DataReader()}(index{item.Identifier.Value});""");
						}

						writer.WriteLine();
						writer.Write($"{model}.Add(new(");
						writer.Indent++;
						var comma = "";
						foreach (var item in properties)
						{
							writer.WriteLine(comma);
							writer.Write($"value{item.Identifier.Value}");
							comma = ",";
						}
						writer.Indent--;
						writer.WriteLine("));");
					});
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
					writer.Block($"""
						System.Text.StringBuilder query = new();
						query.Append("Insert Into @Param{(CodeBlock.IsTable ? "s" : "")}_{CodeBlock.Name}([{string.Join("], [", CodeBlock.Properties.Select(p => p.Identifier.Value))}])");
						""");

					if (CodeBlock.IsTable)
					{
						writer.Block($"""
							if (request.{CodeBlock.Name}.Count() == 0) return "";

							query.AppendLine(" Values");

							var comma = "";
							var index = 0;

							""");
						writer.Block($"foreach(var item in request.{CodeBlock.Name})", () =>
						{
							writer.Block("""
								index++;

								query.AppendLine(comma);
								query.Append('(');
								""");
							AddParams("s", "index", "item");
							writer.Block("""
								query.Append(')');

								comma = ",";
								""");
						});
						writer.WriteLine();
					}
					else if (CodeBlock.IsObject)
					{
						writer.Block($"""
							if (request.{CodeBlock.Name} is null)
								throw new NullReferenceException(
									"{generation.Request.ModelName} is missing the required property {CodeBlock.Name}.");

							query.AppendLine();
							query.Append("Values (");

							""");
						AddParams("", "0", $"request.{CodeBlock.Name}");
						writer.Block("""
					
							query.Append(')');
							""");
					}

					writer.Block("""
						query.AppendLine(";");
						query.AppendLine();

						return query.ToString();
						""");
				});

				void AddParams(string param, string index, string item)
				{
					param = $"Param{param}{CodeBlock.Name}";

					var notFirst = false;
					foreach (var property in CodeBlock.Properties)
					{
						if (notFirst)
							writer.WriteLine($"""query.Append(", ");""");

						writer.Write($"AddParams(query, ");
						writer.Write($"parameters, ");
						writer.Write($"{index}, ");
						writer.Write($"\"{param}\", ");
						writer.Write($"\"{property.Identifier.Value}\", ");
						writer.Write($"{property.Type.SqlDbType(allowNullSize: true)}, ");
						writer.Write($"{item}.{property.Identifier.Value}");

						if (property.Type.Type == TokenType.TYPE_STRING)
							writer.Write($", {property.Type.Value}");

						writer.WriteLine(");");

						notFirst = true;
					}
				}
			}
		}

		string TableDeclaration(string name, CodeBlock block)
		{
			name = name[1..^6].Trim();
			if (name == "Errors")
				name = "Error";

			return $"""
				Declare {block.DatabaseType.Original}(
					[{SQuiLTableTypeDatabaseTagName}{name}__] varchar(max) default('{name}'),
					{string.Join($",{writer.NewLine}\t", block.Properties.Select(p
						=> $"[{p.Identifier.Value}] {p.Type.Original}{(p.IsNullable ? " Null" : "")}"))});
				""";
		}

		void CommandParameters()
		{
			writer.WriteLine("List<DbParameter> parameters = new()");
			writer.WriteLine("{");
			writer.Indent++;

			writer.WriteLine($$"""CreateParameter("@{{SQuiLGenerator.EnvironmentName}}", System.Data.SqlDbType.VarChar, {{SQuiLGenerator.EnvironmentName}}.Length, {{SQuiLGenerator.EnvironmentName}}),""");
			writer.Write($$"""CreateParameter("@{{SQuiLGenerator.Debug}}", System.Data.SqlDbType.Bit, request.Debug || {{SQuiLGenerator.EnvironmentName}} != "Production")""");

			var parameters = Blocks
				.Where(p => p.CodeType == CodeType.INPUT_ARGUMENT)
				.ToList();

			if (parameters.Count == 0)
			{
				writer.Indent--;
				writer.WriteLine();
				writer.WriteLine("};");
				return;
			}

			foreach (var parameter in parameters)
			{
				if (SQuiLGenerator.IsSpecial(parameter.Name))
					continue;

				writer.WriteLine(",");
				writer.Write($$"""CreateParameter("@Param_{{parameter.Name}}", {{parameter.SqlDbType()}}, """);

				WriteValue();

				if (parameter.IsNullable)
					writer.Write($$""", p => p.IsNullable = true""");

				writer.Write(")");

				void WriteValue()
				{
					var value = $"request.{parameter.Name}";

					if (parameter.DatabaseType.Type != TokenType.TYPE_STRING)
					{
						writer.WriteLine($$"""{{value}} ?? (object)System.DBNull.Value""");
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

			writer.WriteLine();
			writer.Indent--;
			writer.WriteLine("};");
		}
		/*
		string F(IEnumerable<string> lines)
			=> string.Join($"{newline}{newline}{tabs}", lines);
		*/
	}
}
