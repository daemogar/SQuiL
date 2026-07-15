using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using SQuiL.VisualStudioExtension.Parsing;

namespace SQuiL.VisualStudioExtension.SuggestedActions;

/// <summary>
/// One light-bulb action wrapping a single
/// <see cref="SQuiLCodeActions.CodeActionEdit"/> (an "Add Primary Key" or a
/// "Link to `&lt;Table&gt;` via `&lt;PK&gt;`" insertion). <see cref="Invoke"/> applies the
/// edit to the buffer on the UI thread, at the position the pure logic
/// computed against the current snapshot's lines.
/// </summary>
internal sealed class SQuiLCodeActionSuggestedAction : ISuggestedAction
{
    private readonly ITextBuffer _buffer;
    private readonly SQuiLCodeActions.CodeActionEdit _edit;

    public SQuiLCodeActionSuggestedAction(ITextBuffer buffer, SQuiLCodeActions.CodeActionEdit edit)
    {
        _buffer = buffer;
        _edit = edit;
    }

    public string DisplayText => _edit.Title;
    public string? IconAutomationText => null;
    public ImageMoniker IconMoniker => default;
    public string? InputGestureText => null;

    public bool HasActionSets => false;
    public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<SuggestedActionSet>>(Array.Empty<SuggestedActionSet>());

    public bool HasPreview => false;
    public Task<object?> GetPreviewAsync(CancellationToken cancellationToken)
        => Task.FromResult<object?>(null);

    public void Invoke(CancellationToken cancellationToken)
    {
        var snapshot = _buffer.CurrentSnapshot;
        int line = _edit.Position.Line;
        if (line < 0 || line >= snapshot.LineCount) return;

        var snapshotLine = snapshot.GetLineFromLineNumber(line);
        int character = _edit.Position.Character;
        if (character < 0 || character > snapshotLine.Length) return;

        int offset = snapshotLine.Start.Position + character;
        if (offset < 0 || offset > snapshot.Length) return;

        using var textEdit = _buffer.CreateEdit();
        textEdit.Insert(offset, _edit.InsertText);
        textEdit.Apply();
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public void Dispose() { }
}
