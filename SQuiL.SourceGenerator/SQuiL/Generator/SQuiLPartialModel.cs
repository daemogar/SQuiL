using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

/// <summary>
/// Snapshot of a <c>record</c> declaration found in the consumer project's compilation.
/// Used to detect user-defined partial records so the generator can merge generated
/// properties with hand-written ones rather than fully re-emitting the type.
/// </summary>
/// <param name="Name">The simple (unqualified) record type name.</param>
/// <param name="Syntax">The raw Roslyn syntax node for the record declaration.</param>
public record SQuiLPartialModel(
	string Name,
	RecordDeclarationSyntax Syntax)
{
	/// <summary>The declaring namespace (dotted), or empty for the global namespace.</summary>
	public string Namespace { get; } = NamespaceOf(Syntax);

	/// <summary><c>true</c> when the record can merge with a generated type of the same name:
	/// declared <c>partial</c> and not nested inside another type.</summary>
	public bool IsMergeable { get; } = Syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
		&& Syntax.Parent is not TypeDeclarationSyntax;

	private static string NamespaceOf(SyntaxNode node)
	{
		var name = "";
		for (var parent = node.Parent; parent is not null; parent = parent.Parent)
			if (parent is BaseNamespaceDeclarationSyntax ns)
				name = name.Length == 0 ? ns.Name.ToString() : $"{ns.Name}.{name}";
		return name;
	}
}
