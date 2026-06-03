using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using SQuiL.VisualStudioExtension.Parsing;
using SQuiL.VisualStudioExtension.SampleData;

namespace SQuiL.VisualStudioExtension.Commands;

/// <summary>
/// Inserts (or replaces in-place) a sample <c>Insert Into @var Values …;</c>
/// block for a table-typed SQuiL variable.
///
/// Triggered from the SQuiL completion list when a "⊕ Insert/Modify sample
/// data → @Var" item is committed.  Not bound to a menu command — invoked
/// directly from the completion filter once it sees the marker completion.
///
/// Behaviour matches the <c>squil.insertSampleData</c> handler in the VS Code
/// extension:
///   • <see cref="VariableRole.Params"/> → prompt the user for row count.
///   • <see cref="VariableRole.ParamTable"/> → always insert exactly 1 row, no prompt.
///   • If a block already exists for the variable, replace it in-place;
///     otherwise insert at the start of the caret line.
/// </summary>
internal static class InsertSampleDataCommand
{
    public static void Execute(IWpfTextView textView, SQuiLVariable variable, bool hasExisting)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (variable.Columns is null || variable.Columns.Count == 0) return;

        // ── 1. Determine row count ────────────────────────────────────────
        int count;
        if (variable.Role == VariableRole.Params)
        {
            var dialog = new RecordCountDialog(variable.RawName, hasExisting);
            if (dialog.ShowModal() != true || dialog.SelectedCount is null) return;
            count = dialog.SelectedCount.Value;
        }
        else if (variable.Role == VariableRole.ParamTable)
        {
            count = 1;
        }
        else
        {
            // Only @Params_ / @Param_ table can have a sample-data block.
            return;
        }

        // ── 2. Generate SQL ──────────────────────────────────────────────
        string sql = SampleDataGenerator.Generate(variable, count);
        if (string.IsNullOrEmpty(sql)) return;

        // ── 3. Apply edit ────────────────────────────────────────────────
        var snapshot = textView.TextBuffer.CurrentSnapshot;
        string[] lines = snapshot.GetText().Split('\n');

        if (hasExisting)
        {
            var found = SampleDataGenerator.FindLines(lines, variable.RawName);
            if (found is null)
            {
                // The completion source said a block existed, but the buffer
                // moved underneath us.  Fall through to a fresh insert.
                hasExisting = false;
            }
            else
            {
                var startLine = snapshot.GetLineFromLineNumber(found.Value.StartLine);
                var endLine   = snapshot.GetLineFromLineNumber(found.Value.EndLine);
                var span = Span.FromBounds(startLine.Start.Position, endLine.End.Position);
                using var edit = textView.TextBuffer.CreateEdit();
                edit.Replace(span, sql);
                edit.Apply();
                return;
            }
        }

        // Fresh insert at the start of the caret line.
        var caret    = textView.Caret.Position.BufferPosition;
        var caretLn  = snapshot.GetLineFromPosition(caret.Position);
        int insertAt = caretLn.Start.Position;
        using (var edit = textView.TextBuffer.CreateEdit())
        {
            edit.Insert(insertAt, sql + "\r\n");
            edit.Apply();
        }
    }
}
