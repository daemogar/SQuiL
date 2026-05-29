using System;
using Microsoft.VisualStudio.Text;

namespace SQuiL.SsmsExtension.ContentType;

/// <summary>
/// .squil files open with SSMS's SQL Query Editor — bound via
/// <c>SQuiL.Bindings.pkgdef</c> — so F5/connection/results-pane all work
/// like a plain .sql file.  The editor pins the buffer content type to
/// <c>SQL</c>; we used to swap it to a SQuiL-specific subtype, but that
/// disabled SSMS's Execute (F5) command, which strict-equality-checks the
/// content type name.
///
/// So instead we leave the buffer alone.  Our MEF components all
/// subscribe to <c>SQL</c> and call <see cref="IsSquilBuffer"/> to
/// short-circuit on ordinary .sql buffers; only buffers whose underlying
/// file ends with <c>.squil</c> get SQuiL highlighting, completion,
/// quick-info, taggers, and commands.
/// </summary>
internal static class SQuiLContentTypeDefinition
{
    /// <summary>The MEF content type SSMS's SQL Query Editor assigns to all .sql/.squil buffers.</summary>
    public const string SqlContentTypeName = "SQL";

    /// <summary>
    /// True iff <paramref name="buffer"/>'s underlying file path ends with
    /// the .squil extension (case-insensitive).  Returns false for ordinary
    /// .sql files or buffers without an associated file (scratch buffers).
    /// </summary>
    public static bool IsSquilBuffer(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        if (!buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc) || doc == null)
            return false;
        return doc.FilePath?.EndsWith(SQuiLPackageGuids.SQuiLFileExtension, StringComparison.OrdinalIgnoreCase) == true;
    }
}
