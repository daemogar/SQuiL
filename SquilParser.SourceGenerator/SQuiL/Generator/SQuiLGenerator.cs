using System.CodeDom.Compiler;
using System.Collections.Generic;
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
	public static string Debug { get; } = nameof(Debug);

	public static string EnvironmentName { get; } = nameof(EnvironmentName);

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

							if ("Microsoft.Extensions.DependencyInjection.dll|Microsoft.Extensions.DependencyInjection.Abstractions.dll".Contains(dll))
								return new SQuiLDependency(dll) { DependencyInjection = true };

							if ("Microsoft.Extensions.Configuration.dll|Microsoft.Extensions.Configuration.Abstractions.dll".Contains(dll))
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
			.AddSource($"{QueryAttributeName}.g.cs", SourceText.From($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
				public class {{QueryAttributeName}} : System.Attribute
				{
					public QueryFiles Type { get; }
		
					public string Setting { get; }

					public {{QueryAttributeName}}(
						QueryFiles type,
						string setting = "{{DefaultConnectionStringAppSettingName}}")
					{
						Type = type;
						Setting = setting;
					}
				}
				""", Encoding.UTF8)));

		context.RegisterPostInitializationOutput(static p => p
			.AddSource($"{TableTypeAttributeName}", SourceText.From($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
				public class {{TableTypeAttributeName}} : System.Attribute
				{
					public TableType Type { get; }
		
					public {{TableTypeAttributeName}}(TableType type)
					{
						Type = type;
					}
				}
				""", Encoding.UTF8)));

		var classes = context.SyntaxProvider
						.CreateSyntaxProvider(
										predicate: static (p, _) => p is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
										transform: static (p, _) => GetSemanticTargetForGeneration(p))
						.SelectMany((p, _) => p!)
						.Where(p => p is not null)
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
				}).ToImmutableArray(), b.Left.Left.Right!, records.ToImmutableDictionary(), a);
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

	private static IEnumerable<SQuiLDefinition?> GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
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

				var definition = SQuiLDefinitionType.Invalid;
				var type = symbol.ContainingType;
				var name = type.ToDisplayString();

				if (name.Equals($"{NamespaceName}.{QueryAttributeName}"))
					definition = SQuiLDefinitionType.Query;

				if (name.Equals($"{NamespaceName}.{TableTypeAttributeName}"))
					definition = SQuiLDefinitionType.TableType;

				if (definition == SQuiLDefinitionType.Invalid)
					continue;

				yield return new(definition, syntax.Modifiers.Any(p => p.ValueText?.Equals("partial") == true), syntax, attribute);
			}
	}

	private void Execute(Compilation compilation, ImmutableArray<SQuiLDependency> dependencies, ImmutableArray<AdditionalText> files, ImmutableArray<SQuiLDefinition> definitions, ImmutableDictionary<string, SQuiLPartialModel> records, SourceProductionContext context)
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
		GenerateQueryFilesEnum(context, files);

		var classes = definitions.Where(p => p.Type == SQuiLDefinitionType.Query).ToImmutableArray();

		if (classes.IsDefaultOrEmpty || files.IsDefaultOrEmpty)
		{
			GenerateDependencyInjectionCode([]);
			GenerateTablesEnum(context, []);
			return;
		}

		List<string> contexts = [];
		ImmutableDictionary<string, SQuiLTableMap> tableMap = classes
			.GroupBy(p => p.Class.Identifier.ValueText)
			.SelectMany(p => p.Select((q, i) => (
				Key: GetValueLocation(q.Attribute.ArgumentList!).Value,
				Value: new SQuiLTableMap(i == 0, p.Key)
			)))
			.ToImmutableDictionary(p => p.Key, p => p.Value);

		// compare all models
		FileGenerator generator = new(ShowDebugMessages, context, tableMap);
		foreach (var definition in classes.Distinct())
		{
			context.CancellationToken.ThrowIfCancellationRequested();

			var list = definition.Attribute.ArgumentList!;

			var setting = (list.Arguments.Skip(1).FirstOrDefault()?.Expression as LiteralExpressionSyntax)?.Token.ValueText.Trim()
				?? DefaultConnectionStringAppSettingName;

			var (method, location) = GetValueLocation(list);
			if (location is null) continue;

			var file = files.FirstOrDefault(p => p.Path.Replace("\\", "").Replace(".sql", "").Equals(method));
			if (file is null) return;

			var text = file.GetText(context.CancellationToken);
			var inherits = definition.Class.BaseList?.Types
				.Any(p => p.ToString() == BaseDataContextClassName
					|| p.ToString().StartsWith($"{BaseDataContextClassName}(")) == true;

			if (!definition.HasPartialKeyword || text is null || !inherits)
			{
				if (text is null)
				{
					context.FileNotFound(file.Path, location);
				}

				if (!inherits)
				{
					context.MissingBaseDataContextDeclaration(location);
				}

				if (!definition.HasPartialKeyword)
				{
					location = definition.Class.Keyword.GetLocation();
					context.MissingPartialDeclaration(location);
				}

				continue;
			}

			var classname = definition.Class.Identifier.ValueText;
			var @namespace = definition.Class.Parent switch
			{
				NamespaceDeclarationSyntax p => p.Name.ToString(),
				FileScopedNamespaceDeclarationSyntax p => p.Name.ToString(),
				_ => default
			};

			if (@namespace is null)
			{
				context.SQuiLClassContextMustHaveNamespace(classname, definition.Class.GetLocation());
				GenerateDependencyInjectionCode([]);
				return;
			}

			if (missingDataClient)
				continue;

			contexts.Add($"{@namespace}.{classname}");
			generator.Create(@namespace, classname, method, setting, text.ToString(), records);
		}

		GenerateDependencyInjectionCode(contexts);
		GenerateTablesEnum(context, generator.Tables);

		(string Value, Location? Location) GetValueLocation(AttributeArgumentListSyntax syntax)
			=> syntax.Arguments[0].Expression switch
			{
				MemberAccessExpressionSyntax member => (member.Name.Identifier.Text, member.GetLocation()),
				IdentifierNameSyntax identifier => (identifier.ToString(), identifier.GetLocation()),
				_ => ("", default(Location))
			};

		void GenerateBaseDataContextClass()
		{
			if (missingConfiguration)
				return;

			context.AddSource($"{BaseDataContextClassName}.g.cs", SourceText.From($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				using Microsoft.Data.SqlClient;
				using Microsoft.Extensions.Configuration;

				public abstract class {{BaseDataContextClassName}}(IConfiguration Configuration)
				{
					//public virtual string SettingName { get; } = "{{DefaultConnectionStringAppSettingName}}";

					protected string {{EnvironmentName}} { get; } = Configuration.GetSection("{{EnvironmentName}}")?.Value
						?? Environment.GetEnvironmentVariable(Configuration.GetSection("EnvironmentVariable")?.Value ?? "ASPNETCORE_ENVIRONMENT")
						?? "Development";
						
					protected SqlConnectionStringBuilder ConnectionStringBuilder(string settingName)
					{
						return new SqlConnectionStringBuilder(Configuration.GetConnectionString(settingName)
							?? throw new Exception($"Cannot find a connection string in the appsettings for {settingName}."));
					}
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
						if (contexts.Count > 0)
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

		static void GenerateQueryFilesEnum(SourceProductionContext context, ImmutableArray<AdditionalText> files)
		{
			StringBuilder sb = new();
			sb.Append($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				public enum QueryFiles
				{
				""");
			var comma = "";
			foreach (var method in files.Where(p => p.Path.EndsWith(".sql")).Select(p => p.Path.Replace("\\", "")))
			{
				sb.AppendLine(comma);
				sb.Append($"\t{method[..^4]}");
				comma = ",";
			}
			sb.AppendLine();
			sb.AppendLine("}");

			context.AddSource($"{NamespaceName}QueryFilesEnum.g.cs", sb.ToString());
		}

		static void GenerateTablesEnum(SourceProductionContext context, IEnumerable<string> tables)
		{
			StringBuilder sb = new();
			sb.Append($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				public enum TableType
				{
				""");
			var comma = "";
			foreach (var table in tables)
			{
				sb.AppendLine(comma);
				sb.Append($"\t{table}");
				comma = ",";
			}
			sb.AppendLine();
			sb.AppendLine("}");

			context.AddSource($"{NamespaceName}TableTypeEnum.g.cs", sb.ToString());
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