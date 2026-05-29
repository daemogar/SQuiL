using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using SQuiL.SsmsExtension.Commands;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// IOleCommandTarget filter that drives the SQuiL completion experience:
///
///   • Typing <c>@</c> opens a completion session (matches VS Code's auto-trigger).
///   • Typing a normal char while a session is open filters the existing list.
///   • Backspace dismisses if it would erase past the trigger.
///   • Enter / Tab commit the highlighted item.
///   • Esc dismisses the session.
///
/// This filter is installed by <see cref="SQuiLCompletionHandlerProvider"/>
/// on every text view created over a "squil" content-type buffer.  It chains
/// to the next command target so all other commands (typing, navigation,
/// SSMS-defined shortcuts) pass through unmodified.
/// </summary>
internal sealed class SQuiLCompletionCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _textView;
    private readonly ICompletionBroker _broker;
    public IOleCommandTarget? Next { get; set; }

    private ICompletionSession? _session;

    public SQuiLCompletionCommandFilter(IWpfTextView textView, ICompletionBroker broker)
    {
        _textView = textView;
        _broker   = broker;
    }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        // IOleCommandTarget contract guarantees UI-thread dispatch.
        ThreadHelper.ThrowIfNotOnUIThread();
        return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? VSConstants.S_OK;
    }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Next == null)
            return (int)Constants.OLECMDERR_E_UNKNOWNGROUP;

        char typedChar = '\0';
        bool isTypeChar =
            pguidCmdGroup == VSConstants.VSStd2K &&
            nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR;

        if (isTypeChar)
            typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

        // ── Commit on Enter / Tab while a session is open ────────────────
        if (_session != null && !_session.IsDismissed && pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.RETURN:
                case VSConstants.VSStd2KCmdID.TAB:
                    if (_session.SelectedCompletionSet?.SelectionStatus?.IsSelected == true)
                    {
                        // Capture the selected completion BEFORE commit — Commit
                        // dismisses the session and the selection becomes null.
                        var selected = _session.SelectedCompletionSet.SelectionStatus.Completion;
                        _session.Commit();

                        // Sample-data marker: ignore the (empty) insertion text
                        // that Commit just applied and run the real edit instead.
                        if (selected is SampleDataCompletion sd)
                            InsertSampleDataCommand.Execute(_textView, sd.Variable, sd.HasExisting);

                        // Tab is fully consumed.  Enter is consumed too — users
                        // press Enter again on a new line if they want one.
                        return VSConstants.S_OK;
                    }
                    _session.Dismiss();
                    break;

                case VSConstants.VSStd2KCmdID.CANCEL:
                    _session.Dismiss();
                    return VSConstants.S_OK;
            }
        }

        // ── Ctrl+Space / Word-completion shortcuts ───────────────────────
        // Intercept BEFORE chaining to Next, so we trigger a session even
        // if SSMS's default handler doesn't.
        if (pguidCmdGroup == VSConstants.VSStd2K)
        {
            switch ((VSConstants.VSStd2KCmdID)nCmdID)
            {
                case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                    DismissExistingSession();
                    TriggerCompletion();
                    _session?.Filter();
                    return VSConstants.S_OK;
            }
        }

        // Pass the keystroke through first so the character is actually typed
        int hr = Next.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

        if (!isTypeChar)
            return hr;

        // ── After-typing hooks ───────────────────────────────────────────
        if (typedChar == '@')
        {
            DismissExistingSession();
            TriggerCompletion();
            _session?.Filter();
        }
        else if (_session != null && !_session.IsDismissed)
        {
            if (IsCommitTrigger(typedChar))
            {
                _session.Dismiss();
            }
            else
            {
                _session.Filter();
            }
        }

        return hr;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void TriggerCompletion()
    {
        var caret = _textView.Caret.Position.BufferPosition;
        _session = _broker.TriggerCompletion(_textView);
        if (_session != null)
            _session.Dismissed += OnSessionDismissed;
    }

    private void DismissExistingSession()
    {
        if (_session != null && !_session.IsDismissed)
            _session.Dismiss();
    }

    private void OnSessionDismissed(object? sender, EventArgs e)
    {
        if (_session != null)
            _session.Dismissed -= OnSessionDismissed;
        _session = null;
    }

    /// <summary>
    /// Characters that close an identifier — we treat them as a hint to commit
    /// the active completion (matches VS's default IntelliSense behaviour for
    /// other languages).
    /// </summary>
    private static bool IsCommitTrigger(char c) =>
        c == ' ' || c == ';' || c == ',' || c == '(' || c == ')' || c == '\t' || c == '\r' || c == '\n';
}
