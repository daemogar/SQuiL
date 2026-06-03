using System;
using Microsoft.VisualStudio.Text;

namespace SQuiL.VisualStudioExtension.ContentType;

/// <summary>
/// In Visual Studio, <c>.squil</c> files are bound to SQL Server Data Tools'
/// (SSDT) T-SQL editor factory via <c>SQuiL.Bindings.pkgdef</c> — the same
/// factory that owns <c>.sql</c> — so opening a <c>.squil</c> file gets the
/// full SQL editing experience: the connection picker + execute toolbar, the
/// results pane, etc., exactly like a <c>.sql</c> file.
///
/// That editor pins its own SQL content type on the buffer.  Rather than swap
/// the buffer to a SQuiL-specific content type — which would break the SQL
/// editor's Execute command (it strict-equality-checks the content type name,
/// see the SSMS port's notes) — our MEF overlay subscribes to the universal
/// <c>text</c> base content type (every text/code content type derives from
/// it, including SSDT's) and calls <see cref="IsSquilBuffer"/> to short-circuit
/// on every buffer that is not a <c>.squil</c> file.  Only <c>.squil</c>
/// buffers get SQuiL highlighting, completion, quick-info, taggers, and
/// commands; ordinary <c>.sql</c> (and all other) files are untouched.
///
/// (The SSMS port subscribes to <c>SQL</c> specifically because SSMS's SQL
/// Query Editor pins exactly that name; in VS the editor host is SSDT, so the
/// host-agnostic <c>text</c> base is the safe choice.)
/// </summary>
internal static class SQuiLContentTypeDefinition
{
    /// <summary>
    /// The content type our MEF components subscribe to.  <c>text</c> is the
    /// root content type that SSDT's SQL content type (and every other
    /// text/code content type) derives from, so subscribing here guarantees we
    /// are offered the SSDT-opened <c>.squil</c> buffer regardless of the exact
    /// name SSDT pins on it.  <see cref="IsSquilBuffer"/> narrows the actual
    /// work back down to <c>.squil</c> files.
    /// </summary>
    public const string OverlayContentTypeName = "text";

    /// <summary>
    /// True iff <paramref name="buffer"/>'s underlying file path ends with the
    /// .squil extension (case-insensitive).  Returns false for ordinary .sql
    /// files or buffers without an associated file (scratch buffers).
    /// </summary>
    public static bool IsSquilBuffer(ITextBuffer buffer)
    {
        if (buffer == null) return false;
        if (!buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc) || doc == null)
            return false;
        return doc.FilePath?.EndsWith(SQuiLPackageGuids.SQuiLFileExtension, StringComparison.OrdinalIgnoreCase) == true;
    }
}
