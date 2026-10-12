using Basic.Reference.Assemblies;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SQuiL.Generator;

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace SQuiL.Tests;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

/// <summary>
/// The generator runs in every project downstream of SQuiL. Each project's <c>AddSQuiL()</c> is
/// internal and registers every context it can see; nothing collides across assemblies (TODO #28, #30).
/// </summary>
public class ConsumerProjectTests
{
	private const string Registrations = "global::Microsoft.Extensions.DependencyInjection";

	private const string LibraryQuery = """
		--Name: GetCount
		Declare @Return_Count int;
		Use [Db];
		Set @Return_Count = 1;
		Select @Return_Count;
		""";

	private const string ConsumerSource = """
		using Microsoft.Extensions.DependencyInjection;

		namespace Consumer;

		public static class Startup
		{
			public static IServiceCollection Configure(IServiceCollection services) => services.AddSQuiL();
		}
		""";

	/// <summary>A data context in <paramref name="ns"/>; grants <c>InternalsVisibleTo("Consumer")</c> unless it is the consumer.</summary>
	private static string ContextSource(string ns, bool grantConsumer = true) => $$"""
		using Microsoft.Extensions.Configuration;
		using {{NamespaceName}};

		{{(grantConsumer ? """[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Consumer")]""" : "")}}

		namespace {{ns}};

		[{{QueryAttributeName}}(QueryFiles.GetCount)]
		public partial class QueryContext(IConfiguration Configuration) : SqlServerDataContext(Configuration)
		{
		}
		""";

	private sealed record Library(string Name, byte[] Image)
	{
		public MetadataReference Reference => MetadataReference.CreateFromImage(Image);
	}

	[Fact]
	public void Project_without_queries_contexts_or_SQuiL_references_gets_no_AddSQuiL_and_no_diagnostics()
	{
		var (_, result) = Compile("Consumer", [ConsumerSource], files: [], references: []);

		Assert.DoesNotContain(result.GeneratedTrees, p => p.FilePath.EndsWith($"{NamespaceName}Extensions.g.cs"));
		Assert.Empty(result.Diagnostics);
	}

	[Fact]
	public void Reference_only_consumer_gets_its_own_AddSQuiL_that_calls_the_library()
	{
		var library = Build("Library");

		var (consumer, result) = Compile("Consumer", [ConsumerSource], [], [library.Reference]);

		AssertClean(consumer);
		Assert.Equal("Consumer", BoundAssembly(consumer));
		var text = DependencyInjectionText(result);
		Assert.Contains($"{Registrations}.SQuiLLibraryExtensions.AddSQuiLLibrary(services);", text);
		Assert.DoesNotContain("[assembly:", text);
	}

	[Fact]
	public void Reference_only_consumer_without_DI_stays_silent()
	{
		var library = Build("Library");

		var (_, result) = Compile("Consumer", ["namespace Consumer; public static class Startup { }"], [],
			[library.Reference], withDependencyInjection: false);

		Assert.DoesNotContain(result.GeneratedTrees, p => p.FilePath.EndsWith($"{NamespaceName}Extensions.g.cs"));
		Assert.Empty(result.Diagnostics);
	}

	[Fact]
	public void Chained_SQuiL_projects_build_cleanly_and_the_host_calls_both()
	{
		var libA = Build("LibA");
		var libB = Build("LibB", libA.Reference);

		var (consumer, result) = Compile("Consumer", [ConsumerSource], [], [libA.Reference, libB.Reference]);

		AssertClean(consumer);
		var text = DependencyInjectionText(result);
		var a = text.IndexOf($"{Registrations}.SQuiLLibAExtensions.AddSQuiLLibA(services);");
		var b = text.IndexOf($"{Registrations}.SQuiLLibBExtensions.AddSQuiLLibB(services);");
		Assert.True(a >= 0 && b > a, text);
	}

	[Fact]
	public void Sibling_SQuiL_projects_build_cleanly_and_the_host_calls_both()
	{
		var libA = Build("LibA");
		var libC = Build("LibC");

		var (consumer, result) = Compile("Consumer", [ConsumerSource], [], [libA.Reference, libC.Reference]);

		AssertClean(consumer);
		var text = DependencyInjectionText(result);
		Assert.Contains($"{Registrations}.SQuiLLibAExtensions.AddSQuiLLibA(services);", text);
		Assert.Contains($"{Registrations}.SQuiLLibCExtensions.AddSQuiLLibC(services);", text);
	}

	[Fact]
	public void Consumer_with_its_own_context_and_internals_access_builds_cleanly()
	{
		var library = Build("Library");

		var (consumer, result) = Compile("Consumer", [ConsumerSource, ContextSource("Consumer", grantConsumer: false)],
			[LibraryQuery], [library.Reference]);

		AssertClean(consumer);
		Assert.Equal("Consumer", BoundAssembly(consumer));
		var text = DependencyInjectionText(result);
		Assert.Contains($"{Registrations}.SQuiLConsumerExtensions.AddSQuiLConsumer(services);", text);
		Assert.Contains($"{Registrations}.SQuiLLibraryExtensions.AddSQuiLLibrary(services);", text);
	}

	[Fact]
	public void Assemblies_that_reduce_to_one_registration_name_report_SP0046()
	{
		var dotted = Build("A.B");
		var plain = Build("AB");

		var (_, result) = Compile("Consumer", [ConsumerSource], [], [dotted.Reference, plain.Reference]);

		var error = Assert.Single(result.Diagnostics, p => p.Id == "SP0046");
		Assert.Equal(DiagnosticSeverity.Error, error.Severity);
		Assert.Contains("'A.B'", error.GetMessage());
		Assert.Contains("'AB'", error.GetMessage());
	}

	[Fact]
	public void Host_AddSQuiL_resolves_every_referenced_context_at_runtime()
	{
		var libA = Build("LibA");
		var libB = Build("LibB", libA.Reference);
		var (consumer, _) = Compile("Consumer", [ConsumerSource], [], [libA.Reference, libB.Reference]);
		AssertClean(consumer);

		ImageLoadContext images = new([libA.Image, libB.Image, Emit(consumer)]);
		try
		{
			var services = new ServiceCollection().AddSingleton<IConfiguration>(new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:SQuiLDatabase"] = "Server=.;Database=Db" })
				.Build());

			images.Type("Consumer", "Consumer.Startup").GetMethod("Configure")!.Invoke(null, [services]);

			using var provider = services.BuildServiceProvider();
			Assert.NotNull(provider.GetService(images.Type("LibA", "LibA.QueryContext")));
			Assert.NotNull(provider.GetService(images.Type("LibB", "LibB.QueryContext")));
		}
		finally
		{
			images.Unload();
		}
	}

	/// <summary>Loads emitted images side by side and resolves their references to each other.</summary>
	private sealed class ImageLoadContext : AssemblyLoadContext
	{
		public ImageLoadContext(IEnumerable<byte[]> images) : base(isCollectible: true)
		{
			foreach (var image in images)
				LoadFromStream(new MemoryStream(image));
		}

		public Type Type(string assembly, string name)
			=> Assemblies.Single(p => p.GetName().Name == assembly).GetType(name, throwOnError: true)!;

		protected override Assembly? Load(AssemblyName name)
			=> Assemblies.FirstOrDefault(p => p.GetName().Name == name.Name);
	}

	/// <summary>Compiles a library named <paramref name="name"/> with one context in namespace <paramref name="name"/>, and emits it.</summary>
	private static Library Build(string name, params MetadataReference[] references)
	{
		var (compilation, _) = Compile(name, [ContextSource(name)], [LibraryQuery], references);
		AssertClean(compilation);
		return new(name, Emit(compilation));
	}

	private static byte[] Emit(Compilation compilation)
	{
		using MemoryStream image = new();
		var emit = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
		Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
		return image.ToArray();
	}

	private static (Compilation Output, GeneratorDriverRunResult Result) Compile(
		string assemblyName,
		IEnumerable<string> sources,
		IEnumerable<string> files,
		IEnumerable<MetadataReference> references,
		bool withDependencyInjection = true)
	{
		var compilation = CSharpCompilation.Create(
			assemblyName,
			sources.Select(p => CSharpSyntaxTree.ParseText(p, cancellationToken: TestContext.Current.CancellationToken)),
			[
				.. Net100.References.All,
				MetadataReference.CreateFromFile(typeof(SQuiLBaseDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(SqlConnection).Assembly.Location),
				MetadataReference.CreateFromFile(typeof(IConfiguration).Assembly.Location),
				.. withDependencyInjection
					? [
						MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
						MetadataReference.CreateFromFile(typeof(ServiceCollection).Assembly.Location),
					]
					: Array.Empty<MetadataReference>(),
				.. references,
			],
			new CSharpCompilationOptions(
				OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable));

		var driver = CSharpGeneratorDriver
			.Create(new SQuiLGenerator(true))
			.AddAdditionalTexts(files.Select(p => (AdditionalText)new AdditionalQuery(p)).ToImmutableArray())
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

		return (output, driver.GetRunResult());
	}

	/// <summary>No errors, and no CS0436 (a generated type leaking across the assembly boundary).</summary>
	private static void AssertClean(Compilation compilation)
	{
		var problems = compilation.GetDiagnostics(TestContext.Current.CancellationToken)
			.Where(p => p.Severity == DiagnosticSeverity.Error || p.Id == "CS0436")
			.ToList();
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	private static string DependencyInjectionText(GeneratorDriverRunResult result)
		=> result.GeneratedTrees.Single(p => p.FilePath.EndsWith($"{NamespaceName}Extensions.g.cs")).ToString();

	/// <summary>The assembly that the consumer's <c>services.AddSQuiL()</c> call binds to.</summary>
	private static string? BoundAssembly(Compilation consumer)
	{
		var token = TestContext.Current.CancellationToken;
		var tree = consumer.SyntaxTrees.First(p => p.ToString().Contains("public static class Startup"));
		var call = tree.GetRoot(token).DescendantNodes().OfType<InvocationExpressionSyntax>()
			.First(p => p.ToString().Contains("AddSQuiL"));
		return consumer.GetSemanticModel(tree).GetSymbolInfo(call, token).Symbol?.ContainingAssembly.Name;
	}
}
