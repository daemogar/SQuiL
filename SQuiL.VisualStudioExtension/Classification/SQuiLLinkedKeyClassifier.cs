using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using SQuiL.VisualStudioExtension.Parsing;

namespace SQuiL.VisualStudioExtension.Classification;

/// <summary>
/// Task 16 — relationship-key coloring. Tags every column NAME span that
/// plays a role in the nested-object PK/FK-by-convention graph (a parent's
/// Primary Key, and every child column that resolves to it) with the
/// <see cref="SQuiLClassificationTypes.SQuiLRelationshipKey"/> classification
/// type — see <see cref="SQuiLLinter.LinkedColumnSpans"/> for the shared
/// key-graph resolution (same graph the SP0033/SP0034/SP0035 diagnostics and
/// the hover-role text already use).
///
/// A SEPARATE classifier from <see cref="SQuiLClassifier"/> (both are layered
/// on the same buffer via MEF, matching the SSMS/VS classifier-aggregation
/// model described in <see cref="SQuiLClassifier"/>'s own doc comment) because
/// this one needs a WHOLE-DOCUMENT key-graph pass, which
/// <see cref="SQuiLClassifier"/> is intentionally not built for (it is
/// per-line and stateless beyond block-comment tracking). Instead this
/// mirrors <see cref="Tagging.SQuiLErrorTagger"/>'s pattern: debounce buffer
/// changes, recompute the whole-document span list once typing pauses, cache
/// it, and translate cached spans onto whatever snapshot SSMS asks about.
/// </summary>
internal sealed class SQuiLLinkedKeyClassifier : IClassifier
{
    // Matches SQuiLErrorTagger's debounce interval (and diagnosticsProvider.ts's
    // 500 ms onDidChangeTextDocument debounce) — recomputing a whole-document
    // key graph on every keystroke made typing visibly laggy in larger files.
    private const int DebounceMs = 500;

    private readonly ITextBuffer _buffer;
    private readonly IClassificationType _relationshipKeyType;
    private readonly DispatcherTimer _debounce;
    private List<ClassificationSpan> _spans = new();

    public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

    public SQuiLLinkedKeyClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
    {
        _buffer = buffer;
        _relationshipKeyType = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLRelationshipKey);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounce.Tick += OnDebounceTick;

        _buffer.Changed += OnBufferChanged;
        Recompute(buffer.CurrentSnapshot); // initial pass is one-time, fine to run now
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object sender, EventArgs e)
    {
        _debounce.Stop();
        var snapshot = _buffer.CurrentSnapshot;
        Recompute(snapshot);
        ClassificationChanged?.Invoke(this,
            new ClassificationChangedEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
    }

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
    {
        if (_spans.Count == 0) return Array.Empty<ClassificationSpan>();

        var results = new List<ClassificationSpan>();
        foreach (var cached in _spans)
        {
            var translated = cached.Span.TranslateTo(span.Snapshot, SpanTrackingMode.EdgeExclusive);
            if (translated.IntersectsWith(span))
                results.Add(new ClassificationSpan(translated, cached.ClassificationType));
        }
        return results;
    }

    private void Recompute(ITextSnapshot snapshot)
    {
        var newSpans = new List<ClassificationSpan>();
        var parsed = SQuiLParser.Parse(snapshot.GetText());

        foreach (var (line, character, length) in SQuiLLinter.LinkedColumnSpans(parsed))
        {
            if (!TryGetSnapshotSpan(snapshot, line, character, length, out var snapshotSpan)) continue;
            newSpans.Add(new ClassificationSpan(snapshotSpan, _relationshipKeyType));
        }

        _spans = newSpans;
    }

    private static bool TryGetSnapshotSpan(ITextSnapshot snapshot, int line, int character, int length, out SnapshotSpan span)
    {
        span = default;
        if (line < 0 || line >= snapshot.LineCount || length <= 0) return false;

        var snapLine = snapshot.GetLineFromLineNumber(line);
        int lineLen = snapLine.Length;
        int startCol = Math.Min(Math.Max(0, character), lineLen);
        int endCol = Math.Min(startCol + length, lineLen);
        if (endCol <= startCol) return false;

        span = new SnapshotSpan(snapshot, snapLine.Start.Position + startCol, endCol - startCol);
        return true;
    }
}
