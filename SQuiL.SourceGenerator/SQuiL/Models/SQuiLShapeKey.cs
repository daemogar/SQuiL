namespace SQuiL.Models;

using SQuiL.SourceGenerator.Parser;

using System.Linq;

/// <summary>
/// Computes the ordered signature key used to route result sets to their declared
/// output buckets. Key = columns joined by '|', each "name:canonicalType", names
/// lower-cased. No length/precision (irrelevant to a successful C# read). The build-time
/// key here MUST match the runtime key produced by SQuiLBaseDataContext.ShapeKey.
/// </summary>
public static class SQuiLShapeKey
{
    /// <summary>Ordered signature of a table/object output block.</summary>
    public static string ShapeKeyOf(CodeBlock block)
        => string.Join("|", block.Properties.Select(p =>
            $"{p.Identifier.Value.ToLowerInvariant()}:{Canonical(p.CSharpType())}"));

    /// <summary>Single-column signature of a scalar return (name = the scalar's declared base name).</summary>
    public static string ScalarKeyOf(string name, string canonicalCSharpType)
        => $"{name.ToLowerInvariant()}:{Canonical(canonicalCSharpType)}";

    /// <summary>
    /// Normalizes a C# type string to its canonical token: strips a trailing '?' so
    /// nullability never affects the key.
    /// </summary>
    public static string Canonical(string csharpType)
        => csharpType.EndsWith("?") ? csharpType.Substring(0, csharpType.Length - 1) : csharpType;
}
