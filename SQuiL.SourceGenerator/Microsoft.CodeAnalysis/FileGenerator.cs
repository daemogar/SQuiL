using Microsoft.CodeAnalysis.Text;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Microsoft.CodeAnalysis;

public class FileGenerator(
	bool ShowDebugMessages,
	SourceProductionContext Context,
	SQuiLTableMap TableMap)
{
	public List<SQuiLFileGeneration> Generations { get; } = [];

	private void AddSource(string filename, string source)
	{
		try
		{
			Context.AddSource($"{filename}.g.cs", SourceText.From(source, Encoding.UTF8));
		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, filename);
		}
	}

	public SQuiLFileGeneration? Create(string @namespace, string classname, string method, string setting, SourceText text, ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		try
		{
			SQuiLFileGeneration generation = new(method, "");

			var tokens = SQuiLTokenizer.GetTokens(text.ToString());
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

			Generations.Add(generation);

			return generation;
		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, method);
			return default;
		}
	}

	public void GenerateCode()
	{
		foreach (var generation in Generations)
		{
			try
			{
				if (generation.Request.GenerateCode().TryGetValue(out var req, out var reqe))
					AddSource(generation.Request.ModelName, req);
				else
					Context.ReportMissingStatement(reqe);

				if (generation.Response.GenerateCode().TryGetValue(out var res, out var rese))
					AddSource(generation.Response.ModelName, res);
				else
					Context.ReportMissingStatement(rese);

				if (generation.Context.GenerateCode(generation).TryGetValue(out var source, out var e))
					AddSource($"{generation.Method}DataContext", source);
				else
					Context.ReportMissingStatement(e);
			}
			catch (DiagnosticException e)
			{
				Context.ReportLexicalParseErrorDiagnostic(e, generation.Method);
			}
		}

		try
		{
			TableMap.GenerateCode(out var tables, out var exceptions);
			foreach (var exception in exceptions)
				Context.ReportMissingStatement(exception);
			foreach (var (table, text) in tables.Select(p => (p.Key, p.Value)))
				AddSource(table, text);
		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, Generations[0].FilePath);
		}
	}
}
