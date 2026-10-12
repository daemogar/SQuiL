using Basic.Reference.Assemblies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SQuiL.Generator;

namespace SQuiL.Tests;

/// <summary>Naming and discovery of per-assembly SQuiL registration methods (TODO #30).</summary>
public class SQuiLRegistrationsTests
{
	[Theory]
	[InlineData("Checklist.API", "ChecklistAPI")]
	[InlineData("My-Lib.Data", "MyLibData")]
	[InlineData("1Lib", "1Lib")]
	[InlineData("Lib_A", "Lib_A")]
	public void Suffix_strips_non_identifier_characters(string assembly, string expected)
	{
		Assert.Equal(expected, SQuiLRegistrations.Suffix(assembly));
		Assert.True(SyntaxFacts.IsValidIdentifier(SQuiLRegistrations.MethodName(assembly)));
		Assert.True(SyntaxFacts.IsValidIdentifier(SQuiLRegistrations.ClassName(assembly)));
	}

	[Fact]
	public void TypeName_is_fully_qualified()
		=> Assert.Equal(
			"global::Microsoft.Extensions.DependencyInjection.SQuiLLibAExtensions",
			SQuiLRegistrations.TypeName("LibA"));

	[Fact]
	public void Scan_reads_markers_from_referenced_assemblies_in_ordinal_order()
	{
		var host = CSharpCompilation.Create("Host", [], [
			.. Net100.References.All,
			MetadataReference.CreateFromFile(typeof(SQuiLRegistrationAttribute).Assembly.Location),
			Marked("Zeta"),
			Marked("Alpha"),
		]);

		var found = SQuiLRegistrations.Scan(host);

		Assert.Equal(["Alpha", "Zeta"], found.Select(p => p.Assembly));
		Assert.Equal("global::Microsoft.Extensions.DependencyInjection.SQuiLAlphaExtensions", found[0].TypeName);
		Assert.Equal("AddSQuiLAlpha", found[0].Method);
	}

	private static MetadataReference Marked(string name)
	{
		var source = $$"""
			[assembly: SQuiL.SQuiLRegistration(typeof(Microsoft.Extensions.DependencyInjection.SQuiL{{name}}Extensions), "AddSQuiL{{name}}")]
			namespace Microsoft.Extensions.DependencyInjection;
			public static class SQuiL{{name}}Extensions { }
			""";
		var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)], [
			.. Net100.References.All,
			MetadataReference.CreateFromFile(typeof(SQuiLRegistrationAttribute).Assembly.Location),
		], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		using MemoryStream image = new();
		var emit = compilation.Emit(image);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
		return MetadataReference.CreateFromImage(image.ToArray());
	}
}
