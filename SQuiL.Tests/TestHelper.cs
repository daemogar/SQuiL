﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace SQuiL.Tests;

public static class TestHelper
{
	public static Task Verify(
		IEnumerable<string> sources,
		IEnumerable<string> files,
		[CallerMemberName] string name = default!,
		[CallerFilePath] string path = default!)
	{
		var syntaxTrees = sources.Select(p => CSharpSyntaxTree.ParseText(p));

		IEnumerable<MetadataReference> metareferences = [
			MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location)
		];

		var additionalFiles = files
			.Select(p => (AdditionalText)(p.StartsWith("--Name:")
				? new AdditionalQuery(p)
				: new AdditionalFile(p)))
			.ToImmutableArray();

		var compilation = CSharpCompilation.Create(
				assemblyName: "Tests",
				references: metareferences,
				syntaxTrees: syntaxTrees);

		var generator = new SQuiLGenerator(true);

		var driver = CSharpGeneratorDriver.Create(generator);

		driver = (CSharpGeneratorDriver)driver.AddAdditionalTexts(additionalFiles);
		driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

		VerifySettings settings = default!;
		if (path is not null)
		{
			path = path[..path.LastIndexOf('\\')] + @$"\{name}\";
			if (!Directory.Exists(path))
				Directory.CreateDirectory(path);
			settings = new();
			settings.UseDirectory(path);
			//settings.UseTypeName("bob");
		}

		return Verifier.Verify(driver, settings);
	}
}

file class AdditionalQuery(string Text) : AdditionalText
{
	public override string Path { get; } = $"{Text[0..Text.IndexOf('\n')].Split(':', 2)[1].Trim()}.sql";

	public override SourceText? GetText(CancellationToken cancellationToken = default)
		=> SourceText.From(Text[Text.IndexOf('\n')..].TrimStart());
}

file class AdditionalFile(string Path) : AdditionalText
{
	public override string Path { get; } = Path;

	public override SourceText? GetText(CancellationToken cancellationToken = default)
	{
		var text = File.ReadAllText($"..\\..\\..\\{Path}");
		return SourceText.From(text);
	}
}
