using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.Collections;
using System.Collections.Immutable;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.Serialization;
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

			if (!TableMap.TableNames.Any(SQuiLGenerator.IsError))
			{
				AddSource("", $$"""
					{{SourceGeneratorHelper.FileHeader}}

					namespace {{SourceGeneratorHelper.NamespaceName}};
							 
					public sealed class SQuiLException(SQuiLError Error) : System.Data.Common.DbException(Error.Message, Error.Number)
					{
						private SQuiLError Error { get; init; } = Error;

						public override Exception GetBaseException() => this;

						public override bool Equals(object obj)
							=> Error.Equals(obj is SQuiLException error ? error.Error : obj);

						public override int GetHashCode() => Error.GetHashCode();

						public override void GetObjectData(
							SerializationInfo info, StreamingContext context)
							=> throw new NotSupportedException();

						public override string HelpLink => "https://github.com/daemogar/SQuiL";

						public override string ToString()
						{
							StringBuilder sb = new();

							sb.AppendFormat($"{GetType().FullName} (0x{HResult:X8}): {Message}");

							sb.AppendLine();

							sb.AppendFormat($"   Number: {Error.Number}, Severity: {Error.Severity}, State: {Error.State}");

							if (!string.IsNullOrWhiteSpace(Error.Procedure))
								sb.AppendFormat($", Procedure: {Error.Procedure}");

							sb.AppendFormat($", Line {Error.Line}");

							var trace = StackTrace;
							if (trace is not null)
							{
								sb.AppendLine();
								sb.Append(trace);
							}

							return sb.ToString();
						}

						public override IDictionary Data
							=> new System.Collections.Generic.Dictionary<string, object>()
							{
								{ "Number", Error.Number },
								{ "Severity", Error.Severity },
								{ "State", Error.State },
								{ "Line", Error.Line },
								{ "Procedure", Error.Procedure },
								{ "Message", Error.Message }
							};

						public override string StackTrace => base.StackTrace;

						public override string Source => {{SourceGeneratorHelper.NamespaceName}};
					}
					""");

				AddSource("SQuiLError", $$"""
					{{SourceGeneratorHelper.FileHeader}}

					namespace {{SourceGeneratorHelper.NamespaceName}};

					public partial record SQuiLError(
						int Number,
						int Severity,
						int State,
						int Line,
						string Procedure,
						string Message)
					{
						public SQuiLException AsException() => new(this);
					}
					""");
			}
		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, Generations[0].FilePath);
		}
	}
}