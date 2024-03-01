using Microsoft.CodeAnalysis.Text;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.Tokenizer;

using SquilParser.SourceGenerator.Parser;

using System.Collections.Immutable;
using System.Text;

namespace Microsoft.CodeAnalysis;

public class FileGenerator(
	bool ShowDebugMessages,
	SourceProductionContext Context,
	ImmutableDictionary<string, SQuiLTableMap> TableMap)
{
	public List<SQuiLFileGeneration> Generations { get; } = [];

	private void AddSource(string filename, string source)
	{
		Context.AddSource(filename, SourceText.From(source, Encoding.UTF8));
	}

	public void Create(string @namespace, string classname, string method, string setting, string text, ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		if (ShowDebugMessages)
			Context.Debug($"{@namespace} :: {classname} :: {setting} :: {method} :: {text}");

		SQuiLFileGeneration generation = new(method, $"{@namespace}.{classname}");

		try
		{
			var tokens = SQuiLTokenizer.GetTokens(text);
			var blocks = SQuiLParser.ParseTokens(tokens);

			if (ShowDebugMessages)
			{
				foreach (var code in blocks) Context.Debug(code.Source());
				foreach (var token in tokens) Context.Debug(token.Expect());
			}

			(generation.Request, generation.Response) = SQuiLModel.Create(@namespace, method, blocks, TableMap, records);

			foreach (var property in generation.Request.Properties.Union(generation.Response.Properties))
				if (property is SQuiLTable table)
					generation.Tables.Add(table);

			generation.Context = new(@namespace, classname, method, setting, blocks);
		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, method);
		}

		Generations.Add(generation);
	}

	public void GenerateCode()
	{
		foreach (var generation in Generations)
		{
			try
			{
				if (generation.Request.GenerateCode().TryGetValue(out var req, out var reqe))
					AddSource(Hint(generation.Request.ModelName), req);
				else
					Context.ReportMissingStatement(reqe);

				if (generation.Response.GenerateCode().TryGetValue(out var res, out var rese))
					AddSource(Hint(generation.Response.ModelName), res);
				else
					Context.ReportMissingStatement(rese);

				foreach (var table in generation.Tables)
				{
					if (table.GenerateCode().TryGetValue(out var value, out var exception))
					{
						AddSource(Hint(table.ModelName), value);
						continue;
					}

					if (exception is not AggregateException aggregate)
						aggregate = new(exception);

					foreach (var ex in aggregate.InnerExceptions)
						Context.ReportMissingStatement(ex);
				}

				if (generation.Context.GenerateCode(generation).TryGetValue(out var source, out var e))
					AddSource(Hint($"{generation.Method}DataContext"), source);
				else
					Context.ReportMissingStatement(e);
			}
			catch (DiagnosticException e)
			{
				Context.ReportLexicalParseErrorDiagnostic(e, generation.Method);
			}

			string Hint(string name) => $"{generation.Scope}.{name}.g.cs";
		}
	}
}
