using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace SQuiL.Generator;

/// <summary>One referenced assembly's generated SQuiL registration method.</summary>
internal sealed record SQuiLRegistrationReference(string Assembly, string TypeName, string Method);

/// <summary>Names and discovers the per-assembly <c>AddSQuiL&lt;Asm&gt;()</c> methods (TODO #30).</summary>
internal static class SQuiLRegistrations
{
	public const string AttributeName = "SQuiL.SQuiLRegistrationAttribute";

	public const string ExtensionsNamespace = "Microsoft.Extensions.DependencyInjection";

	/// <summary>
	/// The assembly name with every character that is not a letter, digit or <c>_</c> removed;
	/// <c>Assembly</c> when nothing is left, so the class never collides with <c>SQuiLExtensions</c>.
	/// </summary>
	public static string Suffix(string assemblyName)
	{
		var suffix = new string(assemblyName.Where(p => char.IsLetterOrDigit(p) || p == '_').ToArray());
		return suffix.Length == 0 ? "Assembly" : suffix;
	}

	public static string ClassName(string assemblyName) => $"SQuiL{Suffix(assemblyName)}Extensions";

	public static string MethodName(string assemblyName) => $"AddSQuiL{Suffix(assemblyName)}";

	public static string TypeName(string assemblyName) => $"global::{ExtensionsNamespace}.{ClassName(assemblyName)}";

	/// <summary>Every <c>[SQuiLRegistration]</c> on a referenced assembly, ordered by assembly name.</summary>
	public static ImmutableArray<SQuiLRegistrationReference> Scan(Compilation compilation)
		=> compilation.SourceModule.ReferencedAssemblySymbols
			.SelectMany(assembly => assembly.GetAttributes()
				.Where(p => p.AttributeClass?.ToDisplayString() == AttributeName
					&& p.ConstructorArguments.Length == 2
					&& p.ConstructorArguments[0].Value is INamedTypeSymbol
					&& p.ConstructorArguments[1].Value is string)
				.Select(p => new SQuiLRegistrationReference(
					assembly.Name,
					((INamedTypeSymbol)p.ConstructorArguments[0].Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
					(string)p.ConstructorArguments[1].Value!)))
			.OrderBy(p => p.Assembly, StringComparer.Ordinal)
			.ToImmutableArray();
}
