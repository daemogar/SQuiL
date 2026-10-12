using System.Text;

using static Microsoft.CodeAnalysis.SourceGeneratorHelper;

namespace SQuiL.Generator;

/// <summary>Renders <c>SQuiLExtensions.g.cs</c>: the public per-assembly registration and the internal <c>AddSQuiL()</c> (TODO #30).</summary>
internal static class SQuiLDependencyInjectionEmitter
{
	public static string Render(string assemblyName, IReadOnlyList<string> contexts, IReadOnlyList<SQuiLRegistrationReference> references)
	{
		var own = contexts.Count > 0;
		var typeName = SQuiLRegistrations.TypeName(assemblyName);
		var method = SQuiLRegistrations.MethodName(assemblyName);
		var docName = assemblyName.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

		StringBuilder sb = new();
		sb.Append(FileHeader);

		if (own)
			sb.AppendLine($"[assembly: global::SQuiL.SQuiLRegistration(typeof({typeName}), \"{method}\")]").AppendLine();

		sb.AppendLine($"namespace {SQuiLRegistrations.ExtensionsNamespace};").AppendLine()
			.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;").AppendLine();

		if (own)
		{
			sb.AppendLine($"/// <summary>Registers the SQuiL data contexts declared in {docName}.</summary>")
				.AppendLine($"public static class {SQuiLRegistrations.ClassName(assemblyName)}")
				.AppendLine("{")
				.AppendLine($"    /// <summary>Registers the SQuiL data contexts declared in {docName} as singletons. Safe to call more than once.</summary>")
				.AppendLine($"    public static IServiceCollection {method}(")
				.AppendLine("        this IServiceCollection services)")
				.AppendLine("    {");

			foreach (var context in contexts)
				sb.AppendLine($"        services.TryAddSingleton<{context}>();");

			sb.AppendLine()
				.AppendLine("        return services;")
				.AppendLine("    }")
				.AppendLine("}")
				.AppendLine();
		}

		sb.AppendLine("/// <summary>Registers every SQuiL data context this project can see.</summary>")
			.AppendLine("[global::Microsoft.CodeAnalysis.EmbeddedAttribute]")
			.AppendLine("internal static class SQuiLExtensions")
			.AppendLine("{")
			.AppendLine("    /// <summary>Registers this project's SQuiL data contexts and those of every SQuiL project it references, as singletons. Safe to call more than once.</summary>")
			.AppendLine("    public static IServiceCollection AddSQuiL(")
			.AppendLine("        this IServiceCollection services)")
			.AppendLine("    {");

		if (own)
			sb.AppendLine($"        {typeName}.{method}(services);");

		foreach (var reference in references)
			sb.AppendLine($"        {reference.TypeName}.{reference.Method}(services);");

		if (own || references.Count > 0)
			sb.AppendLine();

		sb.AppendLine("        return services;")
			.AppendLine("    }")
			.AppendLine("}");

		return sb.ToString();
	}
}
