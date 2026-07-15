using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using SQuiL.SsmsExtension.Parsing;

namespace SQuiL.SsmsExtension.SuggestedActions;

/// <summary>
/// Light-bulb source for the nested-object PK/link authoring aids — the C#
/// mirror of <c>SQuiL.VSCodeExtension/src/providers/codeActionProvider.ts</c>.
///
/// When the cursor is on a table/object variable declaration we offer:
///   • "Add Primary Key" — when the table declares no Primary Key column yet.
///   • "Link to `&lt;Table&gt;` via `&lt;PK&gt;`" — one action per OTHER declared table
///     in the SAME universe whose Primary Key this table doesn't already carry
///     a matching column for.
///
/// VS/SSMS has no interactive QuickPick modal, so the "pick a parent PK" step
/// is represented as ONE <see cref="ISuggestedAction"/> per candidate link
/// target (plus the single "Add Primary Key" action) — the light-bulb list is
/// itself the picker, matching how the VS Code provider offers one action per
/// candidate.
///
/// All edit computation is the pure logic in <see cref="SQuiLCodeActions"/>;
/// this class only hit-tests the cursor line and turns the resulting
/// <see cref="SQuiLCodeActions.CodeActionEdit"/>s into light-bulb actions.
/// </summary>
internal sealed class SQuiLSuggestedActionsSource : ISuggestedActionsSource
{
    private readonly ITextBuffer _buffer;

    public SQuiLSuggestedActionsSource(ITextBuffer buffer) => _buffer = buffer;

#pragma warning disable CS0067 // Edits are never invalidated by this source, so the change event never fires.
    public event EventHandler<EventArgs>? SuggestedActionsChanged;
#pragma warning restore CS0067

    /// <summary>Runs OFF the UI thread — kept cheap/pure (parse + count, no UI),
    /// per the VS SDK contract.</summary>
    public Task<bool> HasSuggestedActionsAsync(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
        => Task.FromResult(ComputeEdits(range).Count > 0);

    /// <summary>Runs synchronously on the UI thread. Groups the offered edits
    /// into a single <see cref="SuggestedActionSet"/>.</summary>
    public IEnumerable<SuggestedActionSet> GetSuggestedActions(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
    {
        var edits = ComputeEdits(range);
        if (edits.Count == 0) return Array.Empty<SuggestedActionSet>();

        var actions = edits
            .Select(e => (ISuggestedAction)new SQuiLCodeActionSuggestedAction(_buffer, e))
            .ToList();

        return new[]
        {
            new SuggestedActionSet(
                PredefinedSuggestedActionCategoryNames.Refactoring,
                actions),
        };
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public void Dispose() { }

    /// <summary>Parses the current snapshot and computes the PK/link edits
    /// offered for the cursor's line. Pure (no UI) so it is safe to call from
    /// both the off-thread <see cref="HasSuggestedActionsAsync"/> and the
    /// on-thread <see cref="GetSuggestedActions"/>.</summary>
    private List<SQuiLCodeActions.CodeActionEdit> ComputeEdits(SnapshotSpan range)
    {
        var snapshot = _buffer.CurrentSnapshot;
        string text = snapshot.GetText();
        string[] lines = text.Split('\n');
        var parsed = SQuiLParser.Parse(text);
        int cursorLine = snapshot.GetLineFromPosition(range.Start.Position).LineNumber;

        var edits = new List<SQuiLCodeActions.CodeActionEdit>();
        foreach (var table in SQuiLCodeActions.AllTableVariables(parsed))
        {
            if (!SQuiLCodeActions.IsCursorOnVariable(lines, table, cursorLine)) continue;

            if (!table.Columns!.Any(c => c.IsPrimaryKey))
            {
                var edit = SQuiLCodeActions.BuildAddPrimaryKeyEdit(lines, table);
                if (edit is not null) edits.Add(edit);
            }

            foreach (var target in SQuiLCodeActions.AvailableLinkTargets(parsed, table))
            {
                var edit = SQuiLCodeActions.BuildInsertLinkColumnEdit(lines, table, target);
                if (edit is not null) edits.Add(edit);
            }
        }
        return edits;
    }
}
