using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Threading;
using SQuiL.VisualStudioExtension.Parsing;

namespace SQuiL.VisualStudioExtension.Tagging;

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
/// time — made typing visibly laggy in larger files.  The recompute itself runs
/// on a background thread: the context-resolver pass reads every .cs file in the
/// project, which froze the UI for 25–200 ms at each typing pause.
/// </summary>
internal sealed class SQuiLErrorTagger : ITagger<IErrorTag>
{
    // Matches diagnosticsProvider.ts (onDidChangeTextDocument → 500 ms setTimeout).
    private const int DebounceMs = 500;

    private readonly ITextBuffer _buffer;
    private readonly DispatcherTimer _debounce;
    private List<TagSpan<IErrorTag>> _tags = new();

    // Bumped per scheduled recompute (UI thread only); a result is applied only if still latest.
    private int _generation;

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
        ScheduleRecompute();
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
        ScheduleRecompute();
    }

    /// <summary>Computes tags for the current snapshot off the UI thread, then
    /// applies them on the UI thread unless a newer recompute has started.</summary>
    private void ScheduleRecompute()
    {
        var snapshot = _buffer.CurrentSnapshot;
        int generation = ++_generation;

        // Read on the UI thread; the ITextDocument is not ours to touch from a worker.
        string? filePath = null;
        if (_buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
            filePath = doc.FilePath;

#pragma warning disable VSSDK007 // MEF part, no package JTF to track it; a recompute cut off at shutdown only loses a tag update.
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await TaskScheduler.Default;
            var tags = Compute(snapshot, filePath);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (generation != _generation) return;
            _tags = tags;
            TagsChanged?.Invoke(this,
                new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }).FileAndForget("SQuiL/ErrorTagger/Recompute");
#pragma warning restore VSSDK007
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

    /// <summary>Parses + lints <paramref name="snapshot"/>. Pure and thread-safe; runs on a
    /// background thread. <paramref name="filePath"/> enables the context-resolver pass
    /// (SP0028/SP0027).</summary>
    private static List<TagSpan<IErrorTag>> Compute(ITextSnapshot snapshot, string? filePath)
    {
        var newTags = new List<TagSpan<IErrorTag>>();
        string text = snapshot.GetText();

        // Resolve the editor dialect (SQL Server vs SQLite) for the owning project the same
        // way QuickInfo/Preview do — SQuiLContextResolver.ResolveDialect (.csproj PackageReference
        // discovery). Threaded into BOTH the parse and the linter so SQLite Create-Temp-Table
        // declarations are recognized, the SQL-Server-only "missing USE" warning does not fire on
        // a SQLite file, and SP0040's severity follows the dialect. Falls back to SQL Server when
        // the buffer has no on-disk path (untitled buffer).
        var dialect = filePath is not null
            ? SQuiLContextResolver.ResolveDialect(filePath)
            : EditorDialect.SqlServer;

        var parsed = SQuiLParser.Parse(text, dialect);
        SQuiLLinter.Lint(text, parsed.Diagnostics, filePath, dialect);

        foreach (var d in parsed.Diagnostics)
        {
            if (!TryGetSpan(snapshot, d, out var span)) continue;
            newTags.Add(new TagSpan<IErrorTag>(
                span,
                new ErrorTag(MapErrorType(d.Severity), d.Message)));
        }

        return newTags;
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
