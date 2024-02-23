using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

public record SQuiLDefinition(
	SQuiLDefinitionType Type,
	bool HasPartialKeyword,
	ClassDeclarationSyntax Class,
	AttributeSyntax Attribute);
