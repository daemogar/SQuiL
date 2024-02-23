using Azure.Core;

using Microsoft.CodeAnalysis;

using SQuiL.Generator;
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
			List<string> tableChecks = [];

			//if (setting != SourceGeneratorHelper.DefaultConnectionStringAppSettingName)
			//{
			//	writer.WriteLine($$"""public override string SettingName { get; } = "{{setting}}";""");
			//	writer.WriteLine();
			//}

			writer.Block($$"""
								public async Task<{{responseModel}}> Process{{method}}Async(
									{{requestModel}} request,
									CancellationToken cancellationToken = default!)
								""", () =>
			{
				writer.Block($$"""
									var builder = ConnectionStringBuilder("{{setting}}");
									using SqlConnection connection = new(builder.ConnectionString);
									var command = connection.CreateCommand();
									command.CommandText = Query();
									""");
				CommandParameters();
				writer.Block($"""

									await connection.OpenAsync(cancellationToken);

									""");
				if (outputs.Count() == 0)
				{
					writer.WriteLine("await command.ExecuteNonQueryAsync(cancellationToken)");
					writer.WriteLine();
					writer.WriteLine("return new();");
				}
				else
				{
					writer.Write($"{responseModel} response = new();");
					writer.WriteLine();
					VariableSetTracking();
					writer.Block("""

						using var reader = await command.ExecuteReaderAsync(cancellationToken);

						do
						""",
						new Action(() =>
						{
							writer.WriteLine("var tableTag = reader.GetName(0);");
							writer.Block($"""if(tableTag.StartsWith("{SQuiLTableTypeDatabaseTagName}"))""", () =>
							{
								writer.Block($"switch (tableTag)",
									() => SwitchStatements(ref tableChecks));
							});
						}),
						"""
						while (await reader.NextResultAsync(cancellationToken));

						""");

					if (tableChecks.Count > 0)
					{
						foreach (var table in tableChecks)
							writer.WriteLine($"""if (!is{table}) throw new Exception("Expected return table `{table}`)");""");
						writer.WriteLine();
					}

					writer.WriteLine("return response;");
				}
				InsertQueries();
				writer.Block($""""
				
									string Query() => $"""
									"""");
				QueryDeclareStatements();
				writer.Block($$""""
									Use [{builder.InitialCatalog}];
							
									{{query}}
									""";
									"""");
			});
		});

		void QueryDeclareStatements()
		{
			foreach (var (CodeBlock, Query) in inputs)
				writer.Block($$"""
					{{Query}}
					{input{{CodeBlock.Name}}()}
					""");
			
			foreach (var output in outputs)
			{
				writer.Block(output.Query);
				writer.WriteLine();
			}
		}

		return text;

		void VariableSetTracking()
		{
			foreach (var (block, query) in outputs.OrderByDescending(p => p.CodeBlock.IsTable))
			{
				writer.WriteLine($"var is{block.Name} = false;");
			}
		}

		void SwitchStatements(ref List<string> tableChecks)
		{
			try
			{
				foreach (var (block, query) in outputs)
				{
					if (block.IsTable)
						tableChecks.Add(block.Name);

					var switchCase = block.Name;
					if ((block.CodeType & CodeType.OUTPUT) == CodeType.OUTPUT)
						switchCase = $"Return_{switchCase}";

					writer.Block($"""case "{SQuiLTableTypeDatabaseTagName}{switchCase}__":""", () =>
					{
						writer.WriteLine($"is{block.Name} = true;");
						writer.WriteLine();
						writer.WriteLine("if (!await reader.ReadAsync(cancellationToken)) break;");
						writer.WriteLine();
						if (block.IsTable)
						{
							foreach (var item in block.Table)
								writer.WriteLine($"""var index{item.Identifier.Value} = reader.GetOrdinal("{item.Identifier.Value}");""");

							writer.WriteLine();
							writer.Block("do", () =>
							{
								writer.Block($"""if(reader.GetString(0) == "{switchCase}")""", () =>
								{
									writer.Write($"response.{block.Name}.Add(new(");
									writer.Indent++;
									var comma = "";
									foreach (var item in block.Table)
									{
										writer.WriteLine(comma);
										writer.Write($"""{item.DataReader()}(index{item.Identifier.Value})""");
										comma = ",";
									}
									writer.Indent--;
									writer.WriteLine("));");
								});
							}, $"""while(await reader.ReadAsync(cancellationToken));""");
						}
						else
						{
							writer.WriteLine($"if (is{block.Name}) throw new Exception(");
							writer.Indent++;
							writer.WriteLine($"\"Already returned value for `{block.Name}`\");");
							writer.Indent--;
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
						writer.WriteLine();
					});
				}
			}
			finally
			{
				writer.WriteLine("""//default: throw new Exception($"Invalid Table `{reader.GetString(0)}`");""");
			}
		}

		void InsertQueries()
		{
			foreach (var (CodeBlock, Query) in inputs)
			{
				writer.WriteLine();
				writer.WriteLine($"string input{CodeBlock.Name}()");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"""if (request.{CodeBlock.Name}.Count == 0) return "";""");
				writer.WriteLine();
				writer.WriteLine($"System.Text.StringBuilder query = new();");
				writer.WriteLine($"""query.Append("Insert Into @{CodeBlock.Name}({string.Join(", ", CodeBlock.Table.Select(p => p.Identifier.Value))}) Values");""");
				writer.WriteLine($"""var comma = "";""");
				writer.WriteLine($"foreach(var item in request.{CodeBlock.Name})");
				writer.WriteLine("{");
				writer.Indent++;
				writer.WriteLine($"query.AppendLine(comma);");
				writer.WriteLine($"""query.Append('(');""");
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
				writer.WriteLine($"""query.Append(')');""");
				writer.WriteLine();
				writer.WriteLine($"""comma = ",";""");
				writer.Indent--;
				writer.WriteLine("}");
				writer.WriteLine();
				writer.WriteLine($"""query.AppendLine(";");""");
				writer.WriteLine($"""query.AppendLine();""");
				writer.WriteLine();
				writer.WriteLine("return query.ToString();");
				writer.Indent--;
				writer.WriteLine("}");
			}
		}

		string TableDeclaration(string name, CodeBlock block) => $"""
			Declare {block.DatabaseType.Original}(
				[{SQuiLTableTypeDatabaseTagName}{name[1..^6]}__] varchar(max) default('{name[1..^6]}'),
				{string.Join($",{writer.NewLine}\t", block.Table.Select(p
					=> $"{p.Identifier.Value} {p.Type.Original}"))});
			""";

		void CommandParameters()
		{
			writer.WriteLine("command.Parameters.AddRange(new SqlParameter[]");
			writer.WriteLine("{");
			writer.Indent++;

			writer.WriteLine($$"""new("{{SQuiLGenerator.EnvironmentName}}", System.Data.SqlDbType.VarChar, {{SQuiLGenerator.EnvironmentName}}.Length) { Value = {{SQuiLGenerator.EnvironmentName}} }, """);
			writer.Write($$"""new("{{SQuiLGenerator.Debug}}", System.Data.SqlDbType.Bit) { Value = {{SQuiLGenerator.EnvironmentName}} != "Production" }, """);

			var parameters = blocks
				.Where(p => p.CodeType == CodeType.INPUT_ARGUMENT)
				.ToList();

			if (parameters.Count == 0)
			{
				writer.Indent--;
				writer.WriteLine();
				writer.WriteLine("});");
				return;
			}

			var comma = $"";
			foreach (var parameter in parameters)
			{
				if (parameter.Name.Equals(SQuiLGenerator.Debug)) continue;
				if (parameter.Name.Equals(SQuiLGenerator.EnvironmentName)) continue;

				writer.WriteLine(comma);
				writer.Write($$"""new("{{parameter.Name}}", {{parameter.SqlDbType()}}) """);

				var value = $"request.{parameter.Name}";

				writer.WriteLine();
				writer.WriteLine("{");
				writer.Indent++;
				if (parameter.IsNullable)
					writer.WriteLine($$"""IsNullable = true,""");
				if (parameter.DatabaseType.Type != TokenType.TYPE_STRING)
				{
					writer.WriteLine($$"""Value = {{value}} ?? (object)System.DBNull.Value""");
				}
				else
				{
					writer.WriteLine($"Value = {value} switch");
					writer.WriteLine("{");
					writer.Indent++;
					writer.WriteLine("null => (object)System.DBNull.Value,");
					writer.WriteLine($$"""{ Length: <= {{parameter.Size}} } => {{value}},""");
					writer.WriteLine("_ => throw new Exception(");
					writer.Indent++;
					writer.WriteLine($""" "Request model data is larger then database size for the property [{parameter.Name}].")"""[1..]);
					writer.Indent -= 2;
					writer.WriteLine("}");
				}
				writer.Indent--;
				writer.WriteLine("}");

				comma = $",";
			}

			writer.Indent--;
			writer.WriteLine("});");
		}
		/*
		string F(IEnumerable<string> lines)
			=> string.Join($"{newline}{newline}{tabs}", lines);
		*/
	}
}
