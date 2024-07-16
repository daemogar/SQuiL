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
	public static string Debug { get; } = nameof(Debug);

	public static string Error { get; } = nameof(Error);

	public static string EnvironmentName { get; } = nameof(EnvironmentName);

	public static bool IsError(string value)
		=> Error.Equals(value) || $"{Error}s".Equals(value);

	public static bool IsSpecial(string value)
	{
		if (Debug.Equals(value)) return true;
		if (EnvironmentName.Equals(value)) return true;
		return IsError(value);
	}

	public SQuiLGenerator() : this(false) { }

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUG
		if (!System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();
#endif
		var rootPath = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (p, _) => p.SyntaxTree.HasCompilationUnitRoot,
				transform: (p, _) => Path.GetDirectoryName(p.SemanticModel.SyntaxTree.FilePath))
			.Where(p => p is not null)
			.Collect();

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
#if DEBUG
				//if (ShowDebugMessages && !System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();
#endif
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

	private void Execute(Compilation _, ImmutableArray<SQuiLDependency> dependencies, ImmutableArray<AdditionalText> files, ImmutableArray<SQuiLDefinition> definitions, ImmutableDictionary<string, SQuiLPartialModel> records, SourceProductionContext context)
	{
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
			GenerateTablesEnum(context, default);
			return;
		}

		List<string> contexts = [];
		SQuiLTableMap tableMap = new();

		foreach (var record in records)
		{
			foreach (var attribute in record.Value.Syntax.AttributeLists
				.SelectMany(p => p.Attributes
				.Select(p => p.ArgumentList?.Arguments.FirstOrDefault()))
				.Where(p => p is not null))
			{
				var table = attribute!.ToString();

				if (!table.StartsWith("TableType."))
					continue;

				tableMap.Add(table[10..], record.Key, record.Value.Syntax.GetLocation());
			}
		}

		foreach (var (classname, (attribute, location)) in definitions
			.Where(p => p.Type == SQuiLDefinitionType.TableType)
			.GroupBy(p => p.Class.Identifier.ValueText)
			.SelectMany(p => p.Select((q, i) => (
				ClassName: p.Key,
				Attribute: GetValueLocation(q.Attribute.ArgumentList!)
			)))
			.Where(p => p.Attribute.Location is not null))
		{
			tableMap.Add(attribute, classname, location!);
		}
		if (tableMap.TryGetMappingIssues(out var issues))
			context.ReportDuplicateTableMap(issues);

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
					continue;
				}

				if (!inherits)
					context.MissingBaseDataContextDeclaration(location);

				if (definition.HasPartialKeyword)
					continue;

				location = definition.Class.Keyword.GetLocation();
				context.MissingPartialDeclaration(location);
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
			var generation = generator
				.Create(@namespace, classname, method, setting, text, records);

			if (generation is not null)
				generation.FilePath = file.Path;
		}

		if (tableMap.TableNames.Any(IsError))
			GenerateResultType();
		GenerateDependencyInjectionCode(contexts);
		GenerateTablesEnum(context, tableMap);

		generator.GenerateCode();

		(string Value, Location? Location) GetValueLocation(AttributeArgumentListSyntax syntax)
			=> syntax.Arguments[0].Expression switch
			{
				MemberAccessExpressionSyntax member => (member.Name.Identifier.Text, member.GetLocation()),
				IdentifierNameSyntax identifier => (identifier.ToString(), identifier.GetLocation()),
				_ => ("", default(Location))
			};

		void GenerateResultType()
		{
			context.AddSource($"{ResultTypeAttributeName}.g.cs", SourceText.From($$""""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				using System.Collections.Generic;
				using System;
				
				public sealed record {{ResultTypeAttributeName}}<T>
				{
					public bool IsValue { get; }
					private T Value { get; } = default!;
					public SQuiLResultType(T value)
					{
						Value = value;
						IsValue = true;
					}

					public bool HasErrors { get; }
					private IReadOnlyList<SQuiLError> Errors { get; } = default!;
					public SQuiLResultType(IReadOnlyList<SQuiLError> errors)
					{
						Errors = errors;
						HasErrors = true;
					}

					public bool TryGetValue(out T value, out IReadOnlyList<SQuiLError> errors)
					{
						value = default!;
						errors = default!;

						if (IsValue)
						{
							value = Value;
							return true;
						}

						errors = Errors;
						return false;
					}
				}
				"""", Encoding.UTF8));
		}

		void GenerateBaseDataContextClass()
		{
			if (missingConfiguration)
				return;

			context.AddSource($"{BaseDataContextClassName}.g.cs", SourceText.From($$""""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				using Microsoft.Data.SqlClient;
				using Microsoft.Extensions.Configuration;

				using System;
				using System.Collections.Generic;
				using System.Data.Common;
				
				public abstract partial class {{BaseDataContextClassName}}(IConfiguration Configuration)
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
					
					public virtual System.Data.Common.DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

					protected void AddParams(System.Text.StringBuilder query, List<SqlParameter> parameters, int index, string table, string name, System.Data.SqlDbType type, object value, int size = 0)
					{
						var parameter = $"@{table}_{index}_{name}";
						query.Append(parameter);

						if (size == 0)
						{
							parameters.Add(new(parameter, type) { Value = value });
							return;
						}

						parameters.Add(new(parameter, type, size) {
							Value = value is null || ((string)value).Length <= size
								? (value ?? "Null")
								: throw new Exception($"""
									ParamsTable model table property at index [{index}] has a string property [{name}]
									with more than {size} characters.
									""")
						});
					}
				}
				"""", Encoding.UTF8));
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
						public static IServiceCollection AddSQuiL(
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

		static void GenerateTablesEnum(SourceProductionContext context, SQuiLTableMap? tableMap)
		{
			StringBuilder sb = new();
			sb.Append($$"""
				{{FileHeader}}
				namespace {{NamespaceName}};
				
				public enum TableType
				{
				""");
			if (tableMap is not null)
			{
				var comma = "";
				foreach (var table in tableMap.TableNames)
				{
					if (IsError(table)) continue;

					sb.AppendLine(comma);
					sb.Append($"\t{table}");
					comma = ",";
				}
				sb.AppendLine();
			}
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