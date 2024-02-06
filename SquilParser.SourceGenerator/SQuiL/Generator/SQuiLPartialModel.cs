using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SQuiL.Generator;

public record SQuiLPartialModel(
	string Name,
	RecordDeclarationSyntax Syntax);
