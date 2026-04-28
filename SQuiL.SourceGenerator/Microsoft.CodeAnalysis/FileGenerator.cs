using Microsoft.CodeAnalysis.Text;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.Collections.Immutable;
using System.Text;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Orchestrates per-query source generation: tokenizes and parses each SQL file,
/// creates the request/response model pair, and then emits all C# source files
/// (request, response, data-context, and shared table types) into the Roslyn
/// <see cref="SourceProductionContext"/>.
/// </summary>
/// <param name="ShowDebugMessages">When <c>true</c>, each parsed token and code block is emitted as an SP0000 debug diagnostic.</param>
/// <param name="Context">The Roslyn production context used to add sources and report diagnostics.</param>
/// <param name="TableMap">The shared table-name mapping accumulated across all queries in the compilation.</param>
public class FileGenerator(
	bool ShowDebugMessages,
	SourceProductionContext Context,
	SQuiLTableMap TableMap)
{
	/// <summary>All successfully parsed query generations queued for code emission.</summary>
	public List<SQuiLFileGeneration> Generations { get; } = [];

	/// <summary>
	/// Writes <paramref name="source"/> as a <c>.g.cs</c> additional source file,
	/// reporting any <see cref="DiagnosticException"/> as a structured diagnostic instead of throwing.
	/// </summary>
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

	/// <summary>
	/// Tokenizes and parses one SQL <paramref name="text"/>, builds the request/response models,
	/// collects table types, and registers a <see cref="SQuiLFileGeneration"/> for later emission.
	/// </summary>
	/// <param name="namespace">The C# namespace of the data-context class.</param>
	/// <param name="classname">The data-context class name.</param>
	/// <param name="method">The SQL method/query name (file name without extension).</param>
	/// <param name="setting">The connection-string configuration key.</param>
	/// <param name="text">The SQL source text to parse.</param>
	/// <param name="records">All partial record declarations visible in the current compilation.</param>
	/// <returns>The new <see cref="SQuiLFileGeneration"/>, or <c>null</c> if parsing failed.</returns>
	public SQuiLFileGeneration? Create(string @namespace, string classname, string method, string setting, SourceText text, ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		try
		{
			SQuiLFileGeneration generation = new(method);

			var tokens = SQuiLTokenizer.GetTokens(text.ToString());
			var blocks = SQuiLParser.ParseTokens(tokens);

			if (ShowDebugMessages)
			{
				foreach (var code in blocks)
					Context.Debug(code.Source());
				foreach (var token in tokens)
					Context.Debug(token.Expect());
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

	/// <summary>
	/// Emits all accumulated source files: request model, response model, and data-context class
	/// for each registered generation, then emits all shared table-type records via
	/// <see cref="SQuiLTableMap.GenerateCode"/>.
	/// </summary>
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