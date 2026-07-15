using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.SuggestedActions;

/// <summary>
/// MEF-exported provider that supplies an <see cref="ISuggestedActionsSource"/>
/// (the light-bulb / Ctrl+. menu) to every .squil buffer, offering the
/// nested-object PK/link authoring aids (Task 16 — parity with the VS Code
/// code-action provider).  Subscribes to the broader <c>SQL</c> content type
/// (so SSMS's SQL Query Editor buffers get queried) and gates on file
/// extension via <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/> so it
/// never fires on non-.squil SQL buffers, keeping SSMS's own SQL commands
/// untouched.
/// </summary>
[Export(typeof(ISuggestedActionsSourceProvider))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
[Name("SQuiL Suggested Actions")]
internal sealed class SQuiLSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
{
    public ISuggestedActionsSource? CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
    {
        if (textBuffer is null || textView is null) return null;
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(textBuffer)) return null;
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            () => new SQuiLSuggestedActionsSource(textBuffer));
    }
}
