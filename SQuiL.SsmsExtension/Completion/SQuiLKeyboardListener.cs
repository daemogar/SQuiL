using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// Backup keyboard listener for Ctrl+Space.
///
/// SSMS's host catches Ctrl+Space at a layer above the editor command
/// pipeline for its own T-SQL "List Members" feature.  The
/// <c>SQuiLCompletionCommandFilter.Exec</c> intercept for
/// <c>VSStd2KCmdID.COMPLETEWORD</c> therefore never sees the keystroke on
/// our content type — confirmed by smoke-testing in SSMS 22.6.
///
/// We sidestep the command pipeline entirely by hooking the WPF text view's
/// <see cref="UIElement.PreviewKeyDown"/> event: when we see Ctrl+Space on
/// a "squil" buffer we trigger our completion broker directly and mark the
/// event handled so SSMS doesn't also process it.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Interactive)]
[Name("SQuiL Keyboard Listener")]
internal sealed class SQuiLKeyboardListener : IWpfTextViewCreationListener
{
    [Import] internal ICompletionBroker CompletionBroker = null!;

    public void TextViewCreated(IWpfTextView textView)
    {
        // Subscribed to SQL (see SQuiLCompletionHandlerProvider doc); skip
        // non-.squil buffers so we don't hijack Ctrl+Space on .sql files.
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(textView.TextBuffer)) return;

        textView.VisualElement.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Space) return;
            if (Keyboard.Modifiers != ModifierKeys.Control) return;

            // Dismiss any session SSMS may have lit up for its own reasons,
            // then trigger our SQuiL session.
            foreach (var existing in CompletionBroker.GetSessions(textView))
                if (!existing.IsDismissed) existing.Dismiss();

            CompletionBroker.TriggerCompletion(textView);
            e.Handled = true;
        };
    }
}
