using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

/// <summary>
/// Immutable snapshot of a class found in the user's compilation that carries a
/// <c>[SQuiLQueryAttribute]</c> or <c>[SQuiLTableAttribute]</c>.  Captured during
/// syntax-provider scanning and consumed by <see cref="SQuiLGenerator"/>.
/// </summary>
/// <param name="Type">Whether this is a query or table-type definition.</param>
/// <param name="HasPartialKeyword">Whether the class declaration includes the <c>partial</c> modifier.</param>
/// <param name="Class">The raw Roslyn syntax node for the class declaration.</param>
/// <param name="Attribute">The specific attribute syntax that triggered this entry.</param>
internal record SQuiLDefinition(
	SQuiLDefinitionType Type,
	bool HasPartialKeyword,
	ClassDeclarationSyntax Class,
	AttributeSyntax Attribute);
