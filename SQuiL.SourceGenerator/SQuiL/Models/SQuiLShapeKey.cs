namespace SQuiL.Models;

using SQuiL.Dialects;
using SQuiL.SourceGenerator.Parser;
using SQuiL.Tokenizer;

using System.Linq;

/// <summary>
/// Computes the ordered signature key used to route result sets to their declared
/// output buckets. Key = columns joined by '|', each "name:canonicalType", names
/// lower-cased. No length/precision (irrelevant to a successful C# read). The build-time
/// key here MUST match the runtime key produced by each provider's <c>NormalizeType</c>
/// override (SqlServerDataContext / SqliteDataContext), reached via SQuiLBaseDataContext.ShapeKey.
/// </summary>
public static class SQuiLShapeKey
{
    /// <summary>
    /// Ordered signature of a table/object output block, using the SQL Server routing
    /// canonicalization (build-time equivalent of <c>Token.CSharpType</c>).
    /// </summary>
    public static string ShapeKeyOf(CodeBlock block)
        => string.Join("|", block.Properties.Select(p =>
            $"{p.Identifier.Value.ToLowerInvariant()}:{Canonical(p.CSharpType())}"));

    /// <summary>
    /// Ordered signature of a table/object output block, dialect-aware. For every dialect
    /// other than SQLite this is identical to <see cref="ShapeKeyOf(CodeBlock)"/> (the actual
    /// C# property type doubles as the routing token). SQLite has only five storage-class
    /// affinities (INTEGER/TEXT/REAL/BLOB/NUMERIC), so a routing token coarser than the
    /// declared C# property type is required for BOOLEAN/DATETIME/GUID columns to stay in
    /// parity with what <c>SqliteDataContext.NormalizeType</c> can actually observe on a
    /// live reader — see <see cref="RoutingType"/>.
    /// </summary>
    public static string ShapeKeyOf(CodeBlock block, ISqlDialect dialect)
        => string.Join("|", block.Properties.Select(p =>
            $"{p.Identifier.Value.ToLowerInvariant()}:{Canonical(RoutingType(p, dialect))}"));

    /// <summary>
    /// Single-column signature of a scalar return, dialect-aware. The scalar counterpart of
    /// <see cref="ShapeKeyOf(CodeBlock, ISqlDialect)"/>: for every dialect other than SQLite this
    /// key is the scalar's declared base name plus its actual C# type, but SQLite's five
    /// storage-class affinities require the same BOOLEAN/DATETIME/GUID coarsening a table column
    /// gets, or the build key (e.g. <c>flag:bool</c>) can never match what
    /// SqliteDataContext.NormalizeType observes on a live reader (<c>long</c>).
    /// </summary>
    public static string ScalarKeyOf(CodeBlock block, ISqlDialect dialect)
        => $"{block.Name.ToLowerInvariant()}:{Canonical(RoutingType(block, dialect))}";

    /// <summary>
    /// Normalizes a C# type string to its canonical token: strips a trailing '?' so
    /// nullability never affects the key.
    /// </summary>
    public static string Canonical(string csharpType)
        => csharpType.EndsWith("?") ? csharpType.Substring(0, csharpType.Length - 1) : csharpType;

    /// <summary>
    /// The build-time routing token for a column under <paramref name="dialect"/>. SQL Server
    /// (and any dialect not explicitly handled) uses the column's actual C# type — unchanged
    /// from <see cref="ShapeKeyOf(CodeBlock)"/>. SQLite collapses BOOLEAN/DATETIME/GUID down to
    /// the storage-class affinity a live SQLite reader reports (INTEGER/TEXT), matching
    /// <c>SqliteDataContext.NormalizeType</c>'s <c>integer/text/real/blob/numeric</c> map —
    /// the column's actual C# property type (bool/DateTime/Guid) is unaffected; only this
    /// routing token is coarsened.
    /// </summary>
    private static string RoutingType(SQuiL.SourceGenerator.Parser.CodeItem p, ISqlDialect dialect)
    {
        if (dialect is not SqliteDialect)
            return p.CSharpType();

        return p.Type.Type switch
        {
            TokenType.TYPE_BOOLEAN => "long",     // SQLite: BOOLEAN has INTEGER affinity
            TokenType.TYPE_DATETIME => "string",  // SQLite: DATE/DATETIME has TEXT affinity
            TokenType.TYPE_GUID => "string",       // SQLite: GUID/UNIQUEIDENTIFIER has TEXT affinity
            _ => p.CSharpType(),
        };
    }

    /// <summary>
    /// The build-time routing token for a SCALAR block under <paramref name="dialect"/>. Same
    /// coarsening rule as the <see cref="CodeItem"/> overload, keyed off the block's own type
    /// token instead of a column's.
    /// </summary>
    private static string RoutingType(CodeBlock block, ISqlDialect dialect)
    {
        if (dialect is not SqliteDialect)
            return block.CSharpType(block.Name);

        return block.DatabaseType.Type switch
        {
            TokenType.TYPE_BOOLEAN => "long",     // SQLite: BOOLEAN has INTEGER affinity
            TokenType.TYPE_DATETIME => "string",  // SQLite: DATE/DATETIME has TEXT affinity
            TokenType.TYPE_GUID => "string",      // SQLite: GUID/UNIQUEIDENTIFIER has TEXT affinity
            _ => block.CSharpType(block.Name),
        };
    }
}
