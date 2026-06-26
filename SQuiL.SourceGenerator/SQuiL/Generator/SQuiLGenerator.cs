using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Generator;

/// <summary>
/// Roslyn incremental source generator that reads <c>[SQuiLQueryAttribute]</c>-decorated classes
/// and <c>.sql</c> additional files, then emits data-context classes, request/response models,
/// table-type records, and DI extension methods.
/// </summary>
/// <param name="ShowDebugMessages">When <c>true</c>, each parsed token and code block is emitted as an SP0000 debug diagnostic during generation.</param>
[Generator]
public class SQuiLGenerator(bool ShowDebugMessages) : IIncrementalGenerator
{
	/// <summary>
	/// Recognized SQuiL source file extensions, ordered longest-first so the full extension is
	/// stripped when both would match a suffix.
	/// </summary>
	private static readonly string[] SqlFileExtensions = [".squil", ".sql"];

	/// <summary>
	/// Returns <c>true</c> if <paramref name="path"/> ends with a recognized SQuiL source extension
	/// (<c>.sql</c> or <c>.squil</c>).
	/// </summary>
	private static bool IsSqlFile(string path)
	{
		foreach (var extension in SqlFileExtensions)
			if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}

	/// <summary>
	/// Strips a recognized SQuiL source extension (<c>.sql</c> or <c>.squil</c>) from the end of
	/// <paramref name="path"/>; returns the path unchanged when no known extension matches.
	/// </summary>
	private static string StripSqlExtension(string path)
	{
		foreach (var extension in SqlFileExtensions)
			if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
				return path[..^extension.Length];
		return path;
	}

	/// <summary>
	/// Collapses a relative path into a flat identifier by removing both Windows (<c>\</c>) and
	/// POSIX (<c>/</c>) directory separators, so a query produces the same generated name
	/// regardless of the build host's OS (a Linux CI runner reports <c>/</c>-separated paths).
	/// </summary>
	private static string FlattenPath(string path)
		=> path.Replace("\\", "").Replace("/", "");

	/// <summary>SQL variable name for the debug-mode flag: <c>Debug</c>.</summary>
	public static string Debug { get; } = nameof(Debug);

	/// <summary>SQL variable name for the environment name: <c>EnvironmentName</c>.</summary>
	public static string EnvironmentName { get; } = nameof(EnvironmentName);

	/// <summary>SQL variable name for the debug-suppression flag: <c>SuppressDebug</c>.</summary>
	public static string SuppressDebug { get; } = nameof(SuppressDebug);

	/// <summary>SQL variable name for the point-in-time parameter: <c>AsOfDate</c>.</summary>
	public static string AsOfDate { get; } = nameof(AsOfDate);

	/// <summary>
	/// Returns <c>true</c> if <paramref name="value"/> is any reserved special variable name:
	/// <c>Debug</c>, <c>SuppressDebug</c>, <c>EnvironmentName</c>, or <c>AsOfDate</c>.
	/// </summary>
	public static bool IsSpecial(string value)
	{
		if (Debug.Equals(value))
			return true;
		if (SuppressDebug.Equals(value))
			return true;
		if (EnvironmentName.Equals(value))
			return true;
		return AsOfDate.Equals(value);
	}

	/// <summary>
	/// Returns <c>true</c> for the four input-side specials that may be declared in a query header
	/// (<c>Debug</c>, <c>SuppressDebug</c>, <c>EnvironmentName</c>, <c>AsOfDate</c>).
	/// </summary>
	public static bool IsInputSpecial(string value)
		=> Debug.Equals(value) || SuppressDebug.Equals(value)
		|| EnvironmentName.Equals(value) || AsOfDate.Equals(value);

	/// <summary>Parameterless constructor used by Roslyn when activating the generator.</summary>
	public SQuiLGenerator() : this(false) { }

	/// <summary>
	/// Called by Roslyn to wire up all incremental value providers.  Registers syntax and
	/// metadata scanners, emits the attribute and enum post-initialization sources, and connects
	/// the combined pipeline to <see cref="Execute"/>.
	/// </summary>
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
#if DEBUG
		//if (!System.Diagnostics.Debugger.IsAttached)
		//	System.Diagnostics.Debugger.Launch();
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
							var dll = p.Display is null ? null : Path.GetFileName(p.Display);

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
						var path = p.Path[index..].TrimStart('\\', '/');

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

	/// <summary>
	/// Syntax-provider transform: if <paramref name="context"/> is a record declaration,
	/// returns a <see cref="SQuiLPartialModel"/> snapshot; otherwise returns <c>null</c>.
	/// </summary>
	private static SQuiLPartialModel? GetSemanticRecordForGeneratation(GeneratorSyntaxContext context)
	{
		var syntax = (RecordDeclarationSyntax)context.Node;
		return new(syntax.Identifier.Text, syntax);
	}

	/// <summary>
	/// Syntax-provider transform: inspects every attribute list on the class node and yields one
	/// <see cref="SQuiLDefinition"/> per recognized SQuiL attribute (<c>[SQuiLQueryAttribute]</c>
	/// or <c>[SQuiLTableAttribute]</c>).
	/// </summary>
	private static IEnumerable<SQuiLDefinition?> GetSemanticTargetForGeneration(GeneratorSyntaxContext context)
	{
		var syntax = (ClassDeclarationSyntax)context.Node;

		foreach (var attributeLists in syntax.AttributeLists)
			foreach (AttributeSyntax attribute in attributeLists.Attributes)
			{
				var symbolInfo = context.SemanticModel.GetSymbolInfo(attribute);
				if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol symbol)
					continue;

				var definition = SQuiLDefinitionType.Invalid;
				var type = symbol.ContainingType;
				var name = type.ToDisplayString();

				if (name.Equals(NamespacedQueryAttributeValue))
					definition = SQuiLDefinitionType.Query;

				else if (name.Equals(NamespacedTableTypeAttributeName))
					definition = SQuiLDefinitionType.TableType;

				else
					continue;

				yield return new(definition, syntax.Modifiers
					.Any(p => p.ValueText?.Equals("partial") == true), syntax, attribute);
			}
	}

	/// <summary>
	/// Main generation entry point called by Roslyn after all incremental providers combine.
	/// Validates required assembly references, generates the <c>QueryFiles</c> and <c>TableType</c>
	/// enums, resolves table-type mappings, and drives <see cref="FileGenerator"/> for each
	/// SQL file / data-context pair.
	/// </summary>
	private void Execute(Compilation compilation, ImmutableArray<SQuiLDependency> dependencies, ImmutableArray<AdditionalText> files, ImmutableArray<SQuiLDefinition> definitions, ImmutableDictionary<string, SQuiLPartialModel> records, SourceProductionContext context)
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

		GenerateQueryFilesEnum(context, files);

		var classes = definitions.Where(p => p.Type == SQuiLDefinitionType.Query).ToImmutableArray();

		if (classes.IsDefaultOrEmpty || files.IsDefaultOrEmpty)
		{
			GenerateDependencyInjectionCode([]);
			GenerateTablesEnum(context, default);
			return;
		}

		List<string> contexts = [];
		HashSet<string> emittedConstructors = [];
		SQuiLTableMap tableMap = new();

		foreach (var record in records)
		{
			var isTableRecord = false;

			foreach (var attribute in record.Value.Syntax.AttributeLists
				.SelectMany(p => p.Attributes
				.Select(p => p.ArgumentList?.Arguments.FirstOrDefault()))
				.Where(p => p is not null))
			{
				var table = attribute!.ToString();

				if (!table.StartsWith("TableType."))
					continue;

				isTableRecord = true;
				tableMap.Add(table[10..], record.Key, record.Value.Syntax.GetLocation());
			}

			// SP0018: the generator owns the table record's parameter list; a user
			// primary constructor would collide with it (CS8863). An empty `()`
			// parameter list is the block-bodied customization pattern and stays legal.
			if (isTableRecord && record.Value.Syntax.ParameterList is { Parameters.Count: > 0 } parameterList)
				context.ReportTableRecordPrimaryConstructor(record.Key, parameterList.GetLocation());
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
			if (location is null)
				continue;

			var file = files.FirstOrDefault(p => StripSqlExtension(FlattenPath(p.Path)).Equals(method));
			if (file is null)
				return;

			var text = file.GetText(context.CancellationToken);

			if (text is null)
			{
				context.FileNotFound(file.Path, location);
				continue;
			}

			if (!definition.HasPartialKeyword)
				context.MissingPartialDeclaration(definition.Class.Keyword.GetLocation());

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

			var symbol = compilation
				.GetSemanticModel(definition.Class.SyntaxTree)
				.GetDeclaredSymbol(definition.Class);

			if (symbol is not null
				&& emittedConstructors.Add(symbol.ToDisplayString())
				&& !symbol.InstanceConstructors.Any(p => !p.IsImplicitlyDeclared))
			{
				EmitConstructor(@namespace, classname);
			}

			var generation = generator
				.Create(@namespace, classname, method, setting, text, records, $"{@namespace}.Models");

			if (generation is not null)
				generation.FilePath = file.Path;
		}

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
					writer.WriteLine("public static bool IsLoaded { get; private set; }");
					writer.WriteLine();
					writer.Block("""
						public static IServiceCollection AddSQuiL(
							this IServiceCollection services)
						""", () =>
					{
						writer.WriteLine("if (IsLoaded) return services;");
						writer.WriteLine("IsLoaded = true;");
						writer.WriteLine();

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

		void EmitConstructor(string @namespace, string classname)
		{
			StringWriter text = new();
			IndentedTextWriter writer = new(text);

			writer.Block($$"""
				{{FileHeader}}using Microsoft.Extensions.Configuration;

				using {{NamespaceName}};

				namespace {{@namespace}};

				partial class {{classname}} : {{BaseDataContextClassName}}
				""",
				() => writer.WriteLine($"public {classname}(IConfiguration Configuration) : base(Configuration) {{ }}"));

			context.AddSource($"{@namespace}.{classname}.Constructor.g.cs", SourceText.From(text.ToString(), Encoding.UTF8));
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
			// Sort ordinally so the emitted enum is deterministic regardless of the
			// order AdditionalFiles arrive (Directory enumeration / MSBuild glob order
			// differs across OSes — e.g. Windows vs Linux CI). Mirrors TableType, which
			// already sorts via SQuiLTableMap.TableNames.
			foreach (var method in files.Where(p => IsSqlFile(p.Path)).Select(p => StripSqlExtension(FlattenPath(p.Path))).OrderBy(p => p, System.StringComparer.Ordinal))
			{
				sb.AppendLine(comma);
				sb.Append($"\t{method}");
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

/// <summary>
/// File-scoped wrapper around an <see cref="AdditionalText"/> that replaces the file path
/// with a root-relative path so the generator can match SQL file names consistently
/// regardless of absolute solution layout.
/// </summary>
/// <param name="Path">The root-relative path to expose on this wrapper.</param>
/// <param name="Original">The original <see cref="AdditionalText"/> whose content is delegated to.</param>
file class SQuiLAdditionalText(
	string Path,
	AdditionalText Original) : AdditionalText
{
	/// <inheritdoc/>
	public override string Path { get; } = Path;

	/// <inheritdoc/>
	public override SourceText? GetText(CancellationToken cancellationToken = default)
		=> Original.GetText(cancellationToken);
}
