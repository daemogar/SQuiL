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
	RecordDeclarationSyntax Syntax);
