using Microsoft.CodeAnalysis;

using SQuiL.Tokenizer;

using SquilParser.SourceGenerator.Parser;

using System.CodeDom.Compiler;
namespace SQuiL.Models;

public class SQuiLDataContext
{
	internal static string SQuiLTableTypeDatabaseTagName => "__SQuiL__Table__Type__";

	internal static ExceptionOrValue<StringWriter> GenerateCode(
		string @namespace,
		string classname,
		string method,
		string setting,
		List<CodeBlock> blocks)
	{
		StringWriter text = new();
		IndentedTextWriter writer = new(text, "\t");

		var database = blocks.FirstOrDefault(p => p.CodeType == CodeType.USING)?.Name;
		if (database is null)
			return new Exception("Missing USE statement.");

		var query = blocks.First(p => p.CodeType == CodeType.BODY)?.Name;
		if (query is null)
			return new Exception("Missing query body.");

		var responseModel = $"{classname}{method}Response";
		var requestModel = $"{classname}{method}Request";

		var inputs = blocks
			.Where(p => p.CodeType == CodeType.INPUT_TABLE)
			.Select(p => (CodeBlock: p, Query: TableDeclaration(p.DatabaseType.Original!, p)));

		var outputs = blocks
			.Where(p => p.CodeType == CodeType.OUTPUT_TABLE
				|| p.CodeType == CodeType.OUTPUT_VARIABLE)
			.Select(p => (CodeBlock: p, Query: p.CodeType == CodeType.OUTPUT_VARIABLE
				? $"Declare {p.DatabaseType.Original};"
				: TableDeclaration(p.DatabaseType.Original!, p)));

		writer.WriteLine($$"""
			{{SourceGeneratorHelper.FileHeader}}
			using Microsoft.Data.SqlClient;

			using {{SourceGeneratorHelper.NamespaceName}};
		
			namespace {{@namespace}};

			""");

		writer.Block($$"""partial class {{classname}} : {{SourceGeneratorHelper.BaseDataContextClassName}}""", () =>
		{
			writer.Block($$"""
								public async Task<{{responseModel}}> Process{{method}}Async(
									{{requestModel}} request,
									CancellationToken cancellationToken = default!)
								""", () =>
			{
				writer.Block($$"""
									using SqlConnection connection = new(ConnectionString);
									var command = connection.CreateCommand();
									command.CommandText = Query();
									""");
				CommandParameters();
				writer.Block($"""

									await connection.OpenAsync(cancellationToken);

									{responseModel} response = new();

									""");
				VariableSetTracking();
				writer.Block("""

									using var reader = await command.ExecuteReaderAsync(cancellationToken);

									do
									""",
					new Action(() =>
					{
						writer.Block($"""
										while (await reader.ReadAsync(cancellationToken)
											&& reader.GetName(0).Equals("{SQuiLTableTypeDatabaseTagName}"))
										""", () =>
						{
							writer.Block("switch (reader.GetString(0))", SwitchStatements);
						});
					}),
					"""
									while (await reader.NextResultAsync());

									return response;

									""");
				InsertQueries();
				writer.Block($""""
				
									string Query() => $"""
									"""");
				QueryDeclareStatements();
				writer.Block($""""
									Use [{database}];
							
									{query}
									""";
									"""");
			});
		});

		void QueryDeclareStatements()
		{
			foreach (var input in inputs)
				writer.Block($$"""
					{{input.Query}}
					{input{{input.CodeBlock.Name}}()}

					""");

			foreach (var output in outputs)
			{
				writer.Block(output.Query);
				writer.WriteLine();
			}
		}

		/*
		var a = $$""""

						string Query() = $""""
							{{string.Join($"{newline}{newline}{tabs}",
								inputs.Select(p => $"{p.Query}{newline}{tabs}{{input{p.CodeBlock.Name}()}}"))}}

							{{string.Join($"{newline}{newline}{tabs}",
								outputs.Select(p => p.Query))}}

							Use [{{database}}];

							{{query}}
							""";
					}
				}
			}
			"""";
		*/

		return text;

		void VariableSetTracking()
		{
			foreach (var (block, query) in outputs.OrderByDescending(p => p.CodeBlock.IsTable))
			{
				writer.WriteLine($"var is{block.Name} = false;");
			}
		}
		/*
		string M()
		{
			StringBuilder sb = new();
			var t = $"{tabs}\t\t";

			sb.AppendLine();
			sb.Append(t[1..]).Append($"return new(");
			var comma = "";
			foreach (var (block, query) in outputs.Where(p => !p.CodeBlock.IsTable))
			{
				sb.AppendLine(comma);
				sb.Append(t).Append($"var{block.Name}");
				comma = ",";
			}
			sb.AppendLine(")");
			sb.Append(t[1..]).Append("{");
			comma = "";
			foreach (var (block, query) in outputs.Where(p => p.CodeBlock.IsTable))
			{
				sb.AppendLine(comma);
				sb.Append(t).Append($"{block.Name} = tab{block.Name}");
				comma = ",";
			}
			sb.AppendLine();
			sb.Append(t[1..]).Append("};");

			return sb.ToString();
		}
		*/
		void SwitchStatements()
		{
			try
			{
				List<string> tableChecks = [];

				foreach (var (block, query) in outputs)
				{
					writer.WriteLine($"""case "{block.Name}":""");
					writer.Indent++;
					if (block.IsTable)
					{
						tableChecks.Add(block.Name);

						writer.Write($"response.{block.Name}.Add(new(");
						writer.Indent++;
						var comma = "";
						foreach (var item in block.Table)
						{
							writer.WriteLine(comma);
							writer.Write(item.DataReader());
							comma = ",";
						}
						writer.Indent--;
						writer.WriteLine("));");
					}
					else
					{
						writer.WriteLine($"if (is{block.Name}) throw new Exception(");
						writer.Indent++;
						writer.WriteLine($"\"Already returned value for `{block.Name}`\");");
						writer.Indent--;
						writer.WriteLine();
						writer.WriteLine($"response.{block.Name} = {block.DataReader()};");
					}
					writer.WriteLine($"is{block.Name} = true;");
					writer.WriteLine("break;");
					writer.WriteLine();
					writer.Indent--;
				}

				foreach (var table in tableChecks)
					writer.WriteLine($"""if (!is{table}) throw new Exception("Expected return table `{table}`)");""");
				if (tableChecks.Count > 0)
					writer.WriteLine();
			}
			finally
			{
				writer.WriteLine("""default: throw new Exception($"Invalid Table `{reader.GetString(0)}`");""");
			}
		}

		void InsertQueries()
		{
			foreach (var (CodeBlock, Query) in inputs)
			{
				writer.WriteLine($"string input{CodeBlock.Name}()");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"System.Text.StringBuilder query = new();");
				writer.WriteLine($"""query.Append("Insert Into @{CodeBlock.Name}({string.Join(", ", CodeBlock.Table.Select(p => p.Identifier.Value))}) Values");""");
				writer.WriteLine($"""var comma = "";""");
				writer.WriteLine($"foreach(var item in request.{CodeBlock.Name})");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"query.AppendLines(comma);");
				writer.WriteLine($"""query.Append("(");""");
				var comma = "";
				foreach (var property in CodeBlock.Table
					.Select(CodeItem.SqlProperty(classname, CodeBlock.Name))
					.Select(p => p.Replace("\r", "").Split('\n')))
				{
					if (comma.Length > 0)
						writer.WriteLine($"""query.Append("{comma}");""");

					writer.Write($"query.Append({property[0]}");

					if (property.Length == 1)
						writer.WriteLine($");");
					else
					{
						foreach (var line in property.Skip(1))
						{
							writer.WriteLine();
							writer.Write(line);
						}
						writer.WriteLine(");");
					}

					comma = ", ";
				}
				writer.WriteLine($"""query.Append(")");""");
				writer.WriteLine($"""comma = ",";""");
				writer.Indent--;
				writer.WriteLine("}");
				writer.WriteLine($"""query.AppendLine(";");""");
				writer.WriteLine("return query.ToString();");
				writer.Indent--;
				writer.WriteLine("}");
			}
		}

		string TableDeclaration(string name, CodeBlock block) => $"""
			Declare {block.DatabaseType.Original}(
				[{SQuiLTableTypeDatabaseTagName}] varchar(max) default('{name[1..^6]}'),
				{string.Join($",{writer.NewLine}\t", block.Table.Select(p
					=> $"{p.Identifier.Value} {p.Type.Original}"))});
			""";

		void CommandParameters()
		{
			var parameters = blocks
				.Where(p => p.CodeType == CodeType.INPUT_ARGUMENT)
				.ToList();

			if (parameters.Count == 0)
				return;

			writer.WriteLine("command.Parameters.AddRange(new SqlParameter[]");
			writer.Write("{");
			writer.Indent++;

			var comma = $"";
			foreach (var parameter in parameters)
			{
				writer.WriteLine(comma);
				writer.Write($$"""new("{{parameter.Name}}", {{parameter.SqlDbType()}}) """);

				var value = $"request.{parameter.Name}";

				if (parameter.DatabaseType.Type != TokenType.TYPE_STRING)
				{
					writer.Write($$"""{ Value = {{value}} }""");
				}
				else
				{
					writer.WriteLine();
					writer.WriteLine("{");
					writer.Indent++;
					writer.WriteLine($"Value = {value} switch");
					writer.WriteLine("{");
					writer.Indent++;
					writer.WriteLine("null => null,");
					writer.WriteLine($$"""{ Length: <= {{parameter.Size}} } => {{value}},""");
					writer.WriteLine("_ => throw new Exception(");
					writer.Indent++;
					writer.WriteLine($""" "Request model data is larger then database size for the property [{parameter.Name}].")"""[1..]);
					writer.Indent -= 2;
					writer.WriteLine("}");
					writer.Indent--;
					writer.Write("}");
				}

				comma = $",";
			}

			writer.Indent--;
			writer.WriteLine();
			writer.WriteLine("});");
		}
		/*
		string F(IEnumerable<string> lines)
			=> string.Join($"{newline}{newline}{tabs}", lines);
		*/
	}
}
