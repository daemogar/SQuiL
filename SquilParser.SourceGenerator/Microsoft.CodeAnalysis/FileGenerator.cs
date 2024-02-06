using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.IdentityModel.Tokens;

using SQuiL.Generator;
using SQuiL.Models;
using SQuiL.Tokenizer;

using SquilParser.SourceGenerator.Parser;

using System.Collections.Immutable;
using System.Text;

namespace Microsoft.CodeAnalysis;

public class FileGenerator(bool ShowDebugMessages, SourceProductionContext Context)
{
	private void AddSource(string filename, string source)
	{
		Context.AddSource(filename, SourceText.From(source, Encoding.UTF8));
	}

	public void Create(string @namespace, string classname, string method, string setting, string text, ImmutableDictionary<string, SQuiLPartialModel> records)
	{
		try
		{
			var isFailed = false;

			var tokens = SQuiLTokenizer.GetTokens(text);
			var blocks = SQuiLParser.ParseTokens(tokens);

			var (Exceptions, Sources) = SQuiLModel.GenerateModelCode(@namespace, classname, method, blocks, records);
			if(Exceptions.Any())
			{
				isFailed = true;
				foreach (var exception in Exceptions)
					Context.ReportMissingStatement(exception);
			}

			var context = SQuiLDataContext.GenerateCode(@namespace, classname, method, setting, blocks);
			if (context.IsException)
			{
				isFailed = true;
				Context.ReportMissingStatement(context.Exception);
			}

			if (isFailed)
				return;

			foreach (var (hint, request) in Sources)
			{
				AddSource(hint, request);
			}
			AddSource($"{@namespace}.{classname}.{method}.g.cs", context.Value.ToString());


			// [CompilerGeneratedd]
			// [EditorBrowsable(EditorBrowsableState.Never)]s

			if (!ShowDebugMessages)
				return;

			Context.Debug($"{@namespace} :: {classname} :: {setting} :: {method} :: {text}");

			foreach (var code in blocks)
				try
				{
					Context.Debug(code.Source());
				}
#pragma warning disable CS0168 // Variable is declared but never used
				catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
				{
					throw;
				}

			foreach (var token in tokens)
				Context.Debug(token.Expect());

		}
		catch (DiagnosticException e)
		{
			Context.ReportLexicalParseErrorDiagnostic(e, method);
		}
	}
}
