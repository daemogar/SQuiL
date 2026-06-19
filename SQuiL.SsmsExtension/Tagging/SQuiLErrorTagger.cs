using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using SQuiL.SsmsExtension.Parsing;

namespace SQuiL.SsmsExtension.Tagging;

/// <summary>
/// Produces <see cref="IErrorTag"/>s for SQuiL diagnostics — port of
/// <c>diagnosticsProvider.ts</c>.  Combines the diagnostics that
/// <see cref="SQuiLParser"/> raises during parse (missing/duplicate USE,
/// unknown variable prefix) with the style-lint passes in
/// <see cref="SQuiLLinter"/> (casing, missing semicolons).
///
/// The full document is parsed + linted to produce tags.  Because that is a
/// whole-document pass, it is <b>debounced</b>: buffer changes only restart an
/// idle timer, and the recompute runs once typing pauses (matching the 500 ms
/// debounce in the canonical <c>diagnosticsProvider.ts</c>).  Recomputing
/// synchronously on every keystroke — and invalidating the entire buffer each
/// time — made typing visibly laggy in larger files.
/// </summary>
internal sealed class SQuiLErrorTagger : ITagger<IErrorTag>
{
    // Matches diagnosticsProvider.ts (onDidChangeTextDocument → 500 ms setTimeout).
    private const int DebounceMs = 500;

    private readonly ITextBuffer _buffer;
    private readonly DispatcherTimer _debounce;
    private List<TagSpan<IErrorTag>> _tags = new();

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public SQuiLErrorTagger(ITextBuffer buffer)
    {
        _buffer = buffer;

        // Created on the UI thread (MEF calls CreateTagger there), so this
        // timer is bound to the UI dispatcher and its Tick fires on the UI
        // thread — safe to raise TagsChanged from.
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
        _debounce.Tick += OnDebounceTick;

        _buffer.Changed += OnBufferChanged;
        Recompute(buffer.CurrentSnapshot); // initial pass is one-time, fine to run now
    }

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        // Coalesce rapid edits: restart the idle timer and let the recompute
        // run once the user stops typing, instead of parsing on every key.
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object sender, EventArgs e)
    {
        _debounce.Stop();
        var snapshot = _buffer.CurrentSnapshot;
        Recompute(snapshot);
        TagsChanged?.Invoke(this,
            new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
    }

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0 || _tags.Count == 0) yield break;
        // The cached tags live on a specific snapshot.  Translate spans to the
        // requested snapshot and yield any tag whose span overlaps the query.
        foreach (var tag in _tags)
        {
            var translated = tag.Span.TranslateTo(spans[0].Snapshot, SpanTrackingMode.EdgeExclusive);
            foreach (var span in spans)
            {
                if (translated.IntersectsWith(span))
                {
                    yield return new TagSpan<IErrorTag>(translated, tag.Tag);
                    break;
                }
            }
        }
    }

    // ── Recompute logic ───────────────────────────────────────────────────

    private void Recompute(ITextSnapshot snapshot)
    {
        var newTags = new List<TagSpan<IErrorTag>>();
        string text = snapshot.GetText();

        var parsed = SQuiLParser.Parse(text);
        SQuiLLinter.Lint(text, parsed.Diagnostics);
        SQuiLLinter.LintColumnDefaults(parsed.Variables, parsed.Diagnostics);

        foreach (var d in parsed.Diagnostics)
        {
            if (!TryGetSpan(snapshot, d, out var span)) continue;
            newTags.Add(new TagSpan<IErrorTag>(
                span,
                new ErrorTag(MapErrorType(d.Severity), d.Message)));
        }

        _tags = newTags;
    }

    /// <summary>
    /// Map our diagnostic line/column coordinates to a snapshot span,
    /// clamping past-end-of-line offsets so an "end-of-statement" diagnostic
    /// (start == end at the trimmed line length) still produces a tag of
    /// width >= 1.
    /// </summary>
    private static bool TryGetSpan(ITextSnapshot snapshot, SQuiLDiagnostic d, out SnapshotSpan span)
    {
        span = default;
        if (d.Line < 0 || d.Line >= snapshot.LineCount) return false;

        var line = snapshot.GetLineFromLineNumber(d.Line);
        int lineLen = line.Length;
        int startCol = Math.Min(Math.Max(0, d.StartChar), lineLen);
        int endCol   = Math.Min(Math.Max(startCol, d.EndChar), lineLen);

        if (endCol == startCol)
        {
            // Empty-width diagnostics (e.g. missing semicolon) — expand to
            // cover at least one character so the squiggle is visible.  If
            // there is nothing left on the line, walk one char to the left.
            if (endCol < lineLen)
                endCol = startCol + 1;
            else if (startCol > 0)
                startCol -= 1;
            else
                endCol = startCol + 1;
        }

        int spanStart = line.Start.Position + startCol;
        int spanLen   = endCol - startCol;
        if (spanStart + spanLen > snapshot.Length)
            spanLen = snapshot.Length - spanStart;
        if (spanLen <= 0) return false;

        span = new SnapshotSpan(snapshot, spanStart, spanLen);
        return true;
    }

    private static string MapErrorType(DiagnosticSeverity s) => s switch
    {
        DiagnosticSeverity.Error   => PredefinedErrorTypeNames.SyntaxError,
        DiagnosticSeverity.Warning => PredefinedErrorTypeNames.Warning,
        // Info-level uses the "hinted suggestion" type — renders as a quiet
        // grey/dotted squiggle in SSMS rather than the loud red/green.
        _                          => PredefinedErrorTypeNames.HintedSuggestion,
    };
}
