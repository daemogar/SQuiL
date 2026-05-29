using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using SQuiL.SsmsExtension.ContentType;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// MEF-exported view creation listener.  Installs
/// <see cref="SQuiLCompletionCommandFilter"/> into the editor's command
/// chain so SQuiL auto-triggers IntelliSense on <c>@</c> and routes
/// Tab/Enter/Esc through the active session.
///
/// Subscribes to <c>SQL</c> because every .squil buffer lives with that
/// content type (assigned by SSMS's SQL Query Editor) — see
/// <see cref="SQuiLContentTypeDefinition"/>.  Gating on
/// <see cref="SQuiLContentTypeDefinition.IsSquilBuffer"/> avoids attaching
/// our filter to ordinary .sql buffers.
/// </summary>
[Export(typeof(IVsTextViewCreationListener))]
[ContentType(SQuiLContentTypeDefinition.SqlContentTypeName)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
[Name("SQuiL Completion Handler")]
internal sealed class SQuiLCompletionHandlerProvider : IVsTextViewCreationListener
{
    [Import] internal IVsEditorAdaptersFactoryService AdapterService = null!;
    [Import] internal ICompletionBroker CompletionBroker = null!;
    [Import] internal SVsServiceProvider ServiceProvider = null!;

    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        var textView = AdapterService.GetWpfTextView(textViewAdapter);
        if (textView == null) return;
        if (!SQuiLContentTypeDefinition.IsSquilBuffer(textView.TextBuffer)) return;

        textView.Properties.GetOrCreateSingletonProperty(() =>
        {
            var filter = new SQuiLCompletionCommandFilter(textView, CompletionBroker);
            // AddCommandFilter returns the next-in-chain filter; we route to it.
            textViewAdapter.AddCommandFilter(filter, out IOleCommandTarget next);
            filter.Next = next;
            return filter;
        });
    }
}
