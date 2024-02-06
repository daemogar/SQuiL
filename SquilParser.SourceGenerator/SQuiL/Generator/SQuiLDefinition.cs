using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

public record SQuiLDefinition(
		bool HasPartialKeyword,
		ClassDeclarationSyntax Class,
		AttributeSyntax Attribute);
