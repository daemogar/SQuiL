using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Generator;

[Generator]
public class SQuiLGenerator(bool ShowDebugMessages) : IIncrementalGenerator
{
	public SQuiLGenerator() : this(false) { }

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var rootPath = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (p, _) => p.SyntaxTree.HasCompilationUnitRoot,
				transform: (p, _) => Path.GetDirectoryName(p.SemanticModel.SyntaxTree.FilePath))
			.Where(p => p is not null)
			.Collect();

		//if (!Debugger.IsAttached) Debugger.Launch();

		IncrementalValueProvider<ImmutableArray<SQuiLDependency>> meta = context
						.MetadataReferencesProvider
						.Select(static (p, _) =>
						{
							var dll = p.Display?[(p.Display.LastIndexOf('\\') + 1)..];

							if (dll is null)
								return default;

							if (dll.Equals("Microsoft.Extensions.DependencyInjection.dll"))
								return new SQuiLDependency(dll) { DependencyInjection = true };

							if (dll.Equals("Microsoft.Extensions.Configuration.dll"))
								return new SQuiLDependency(dll) { Configuration = true };

							if (dll.Equals("Microsoft.Data.SqlClient.dll"))
								return new SQuiLDependency(dll) { DataSqlClient = true };

							return default;
						})
						.Where(static p => p is not null)
						.Collect()!;

		var files = context.AdditionalTextsProvider
						.Select(static (p, c) => p)
						.Collect();

		context.RegisterPostInitializationOutput(static p => p
			.AddSource(AttributeFile, SourceText.From($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				[System.AttributeUsage(System.AttributeTargets.Class)]
				public class {{AttributeName}} : System.Attribute
				{
					public QueryFiles Type { get; }
		
					public string Setting { get; }

					public {{AttributeName}}(
						QueryFiles type,
						string setting = "{{DefaultConnectionStringAppSettingName}}")
					{
						Type = type;
						Setting = setting;
					}
				}
				""", Encoding.UTF8)));

		var classes = context.SyntaxProvider
						.CreateSyntaxProvider(
										predicate: static (p, _) => p is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
										transform: static (p, _) => GetSemanticTargetForGeneration(p))
						.Where(p => p is not null)
						.Select((p, _) => p!)
						.Collect();

		var records = context.SyntaxProvider
						.CreateSyntaxProvider(
										predicate: static (p, _) => p is RecordDeclarationSyntax,
										transform: static (p, _) => GetSemanticRecordForGeneratation(p))
						.Where(p => p is not null)
						.Select((p, _) => p!)
						.Collect();

		var compilation = context.CompilationProvider.Combine(meta).Combine(files).Combine(classes).Combine(records).Combine(rootPath);

		context.RegisterSourceOutput(compilation, (a, b) =>
		{
			try
			{
				var rootPaths = b.Right.Distinct().OrderByDescending(p => p.Length).ToArray();
				Dictionary<string, SQuiLPartialModel> records = [];
				foreach (var record in b.Left.Right)
					if (!records.ContainsKey(record.Name))
						records.Add(record.Name, record);

				Execute(b.Left.Left.Left.Left.Left, b.Left.Left.Left.Left.Right, b.Left.Left.Left.Right.Select(p =>
				{
					foreach (var rootPath in rootPaths)
					{
						if (!p.Path.Contains(rootPath))
							continue;

						var index = p.Path.IndexOf(rootPath) + rootPath.Length;
						var path = p.Path[index..].TrimStart('\\');

						return new SQuiLAdditionalText(path, p);
					}

					return p;
				}).ToImmutableArray(), b.Left.Left.Right, records.ToImmutableDictionary(), a);
			}
			catch (Exception e)
			{
				a.CriticalGenerationFailure(e);
				if (ShowDebugMessages && !System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();
			}
		});
	}

	private static SQuiLPartialModel? GetSemanticRecordForGeneratation(GeneratorSyntaxContext context)
	{
		var syntax = (RecordDeclarationSyntax)context.Node;
		return new(syntax.Identifier.Text, syntax);
	}

	private static SQuiLDefinition? GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
	{
		var syntax = (ClassDeclarationSyntax)context.Node;

		foreach (var attributeLists in syntax.AttributeLists)
			foreach (AttributeSyntax attribute in attributeLists.Attributes)
			{
				if (context.SemanticModel
						.GetSymbolInfo(attribute)
						.CandidateSymbols
						.FirstOrDefault() is not IMethodSymbol symbol)
					continue;

				var type = symbol.ContainingType;
				var name = type.ToDisplayString();

				if (!name.Equals(AttributeFQDN))
					continue;

				return new(syntax.Modifiers.Any(p => p.ValueText?.Equals("partial") == true), syntax, attribute);
			}

		return default;
	}

	private void Execute(Compilation compilation, ImmutableArray<SQuiLDependency> dependencies, ImmutableArray<AdditionalText> files, ImmutableArray<SQuiLDefinition> classes, ImmutableDictionary<string, SQuiLPartialModel> records, SourceProductionContext context)
	{
		//if (!System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();

		var missingDependencyInjectable = !dependencies.Any(p => p?.DependencyInjection == true);
		if (missingDependencyInjectable)
			context.ReportNoMicrosoftExtensionsDependencyInjectionDll();

		var missingConfiguration = !dependencies.Any(p => p?.Configuration == true);
		if (missingConfiguration)
			context.ReportNoMicrosoftExtensionsConfigurationDll();

		var missingDataClient = !dependencies.Any(p => p?.DataSqlClient == true);
		if (missingDataClient)
			context.ReportNoMicrosoftDataSqlClientDll();

		GenerateBaseDataContextClass();
		GenerateQueryFilesEnum(files, context);

		if (classes.IsDefaultOrEmpty || files.IsDefaultOrEmpty)
		{
			GenerateDependencyInjectionCode([]);
			return;
		}

		List<string> contexts = [];

		FileGenerator generator = new(ShowDebugMessages, context);
		foreach (var syntax in classes.Distinct())
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var list = syntax.Attribute.ArgumentList!;

			var setting = (list.Arguments.Skip(1).FirstOrDefault()?.Expression as LiteralExpressionSyntax)?.Token.ValueText.Trim()
				?? DefaultConnectionStringAppSettingName;

			var (method, location) = list.Arguments[0].Expression switch
			{
				MemberAccessExpressionSyntax member => (member.Name.Identifier.Text, member.GetLocation()),
				IdentifierNameSyntax identifier => (identifier.ToString(), identifier.GetLocation()),
				_ => ("", default(Location))
			};
			if (location is null) continue;

			var file = files.FirstOrDefault(p => p.Path.Replace("\\", "").Replace(".sql", "").Equals(method));
			if (file is null) return;

			var text = file.GetText(context.CancellationToken);
			var inherits = syntax.Class.BaseList?.Types
				.Any(p => p.ToString() == BaseDataContextClassName
					|| p.ToString().StartsWith($"{BaseDataContextClassName}(")) == true;

			if (!syntax.HasPartialKeyword || text is null || !inherits)
			{
				if (text is null)
				{
					context.FileNotFound(file.Path, location);
				}

				if (!inherits)
				{
					context.MissingBaseDataContextDeclaration(location);
				}

				if (!syntax.HasPartialKeyword)
				{
					location = syntax.Class.Keyword.GetLocation();
					context.MissingPartialDeclaration(location);
				}

				continue;
			}

			var classname = syntax.Class.Identifier.ValueText;
			var @namespace = syntax.Class.Parent switch
			{
				NamespaceDeclarationSyntax p => p.Name.ToString(),
				FileScopedNamespaceDeclarationSyntax p => p.Name.ToString(),
				_ => default
			};

			if (@namespace is null)
			{
				context.SQuiLClassContextMustHaveNamespace(classname, syntax.Class.GetLocation());
				GenerateDependencyInjectionCode([]);
				return;
			}

			if (missingDataClient)
				continue;

			contexts.Add($"{@namespace}.{classname}");
			generator.Create(@namespace, classname, method, setting, text.ToString(), records);
		}

		GenerateDependencyInjectionCode(contexts);

		void GenerateBaseDataContextClass()
		{
			if (missingConfiguration)
				return;

			context.AddSource($"{BaseDataContextClassName}.g.cs", SourceText.From($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				using Microsoft.Extensions.Configuration;

				public abstract class {{BaseDataContextClassName}}(IConfiguration Configuration)
				{
					protected string ConnectionString { get; } = Configuration.GetConnectionString("SQuiLDatabase") ?? throw new Exception("Cannot find a connection string in the appsettings for SQuiLDatabase.");
				}
				""", Encoding.UTF8));
		}

		void GenerateDependencyInjectionCode(List<string> contexts)
		{
			if (missingDependencyInjectable || missingConfiguration)
				return;

			if (contexts.Count == 0)
				context.ReportNoDataContextUsage();

			StringWriter text = new();
			IndentedTextWriter writer = new(text);

			writer.Block($"""
				{FileHeader}
				namespace Microsoft.Extensions.DependencyInjection;
				
				public static class {NamespaceName}Extensions
				""",
				() =>
				{
					writer.WriteLine("public static bool IsLoaded => true;");
					writer.WriteLine();
					writer.Block("""
						public static IServiceCollection AddSQuiLParser(
							this IServiceCollection services)
						""", () =>
					{
						if (contexts.Count == 0)
						{
							foreach (var singleton in contexts.Distinct())
								writer.WriteLine($"services.AddSingleton<{singleton}>();");

							writer.WriteLine();
						}

						writer.WriteLine("return services;");
					});
				});

			context.AddSource($"{NamespaceName}Extensions.g.cs", SourceText.From(text.ToString(), Encoding.UTF8));
		}

		static void GenerateQueryFilesEnum(ImmutableArray<AdditionalText> files, SourceProductionContext context)
		{
			StringBuilder sb = new();
			sb.Append($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				public enum QueryFiles
				{
				""");
			var comma = "";
			foreach (var method in files.Select(p => p.Path.Replace("\\", "")))
			{
				sb.AppendLine(comma);
				sb.Append($"\t{method[..^4]}");
				comma = ",";
			}
			sb.AppendLine();
			sb.AppendLine("}");

			context.AddSource($"{NamespaceName}QueryFilesEnum.g.cs", sb.ToString());
		}
	}
}

file class SQuiLAdditionalText(
	string Path,
	AdditionalText Original) : AdditionalText
{
	public override string Path { get; } = Path;

	public override SourceText? GetText(CancellationToken cancellationToken = default)
		=> Original.GetText(cancellationToken);
}