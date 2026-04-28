using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

internal record SQuiLDefinition(
	SQuiLDefinitionType Type,
	bool HasPartialKeyword,
	ClassDeclarationSyntax Class,
	AttributeSyntax Attribute);
