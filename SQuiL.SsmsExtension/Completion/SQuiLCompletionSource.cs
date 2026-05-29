using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using SQuiL.SsmsExtension.Parsing;
using SQuiL.SsmsExtension.SampleData;

// `SQuiL.SsmsExtension.Completion` is also a namespace — alias the editor
// type so unqualified `Completion` keeps working in this file.
using EditorCompletion = Microsoft.VisualStudio.Language.Intellisense.Completion;

namespace SQuiL.SsmsExtension.Completion;

/// <summary>
/// SQuiL completion source.  Port of <c>completionProvider.ts</c>; the same
/// header/body context detection and the same suggestion set so muscle memory
/// from VS Code transfers to SSMS.
///
/// Snippet placeholders (the <c>${1:Name}</c> markers VS Code uses) are NOT
/// supported by the VS classic intellisense session — accepting a suggestion
/// inserts plain text.  Users overwrite the example fragments by hand.
/// </summary>
internal sealed class SQuiLCompletionSource : ICompletionSource
{
    private static readonly Regex AtTokenAtEnd       = new(@"@(\w*)$", RegexOptions.Compiled);
    private static readonly Regex AtTokenContinues   = new(@"^\w",     RegexOptions.Compiled);
    private static readonly Regex DeclareLine        = new(@"^\s*DECLARE\s+",       RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeclareEmptyTail   = new(@"^\s*DECLARE\s+$",      RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DeclareTypePosition = new(@"DECLARE\s+@\w+\s+$",  RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AsTypePosition     = new(@"\bAS\s+$",             RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WithOpenParen      = new(@"WITH\s*\($",           RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UseLine            = new(@"^\s*USE\s+",           RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BlankOrSqPrefix    = new(@"^\s*(sq)?$",           RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingWord       = new(@"\b(\w+)$",             RegexOptions.Compiled);

    private readonly SQuiLCompletionSourceProvider _provider;
    private readonly ITextBuffer _buffer;
    private bool _disposed;

    public SQuiLCompletionSource(SQuiLCompletionSourceProvider provider, ITextBuffer buffer)
    {
        _provider = provider;
        _buffer   = buffer;
    }

    public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
    {
        if (_disposed) return;

        var snapshot = _buffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(snapshot);
        if (triggerPoint == null) return;

        int caret      = triggerPoint.Value.Position;
        var line       = snapshot.GetLineFromPosition(caret);
        string lineText = line.GetText();
        int columnIndex = caret - line.Start.Position;
        string textBefore = lineText.Substring(0, Math.Min(columnIndex, lineText.Length));
        string textAfter  = columnIndex < lineText.Length ? lineText.Substring(columnIndex) : "";
        bool inHeader = IsInHeader(snapshot, line.LineNumber);

        // Editing inside an existing @word? Suppress so changing Param↔Params
        // is a plain text edit, not a hijacked autocomplete (matches VS Code).
        if (AtTokenAtEnd.IsMatch(textBefore) && AtTokenContinues.IsMatch(textAfter))
            return;

        var atMatch = AtTokenAtEnd.Match(textBefore);

        var completions = new List<EditorCompletion>();
        ITrackingSpan applicableTo = ComputeApplicableTo(snapshot, caret, atMatch, lineText, line.Start.Position);

        // Contribute SQuiL completions ONLY in contexts that are
        // SQuiL-specific.  In every other context we contribute nothing
        // and let SSMS's native SQL IntelliSense (table/column/function
        // suggestions) drive the dropdown — the extension is an
        // augmentation of the SQL editor, not a replacement.
        if (!inHeader)
        {
            // Body: only contribute when the user types @ (we offer the
            // variables they've declared above the cursor).  All other
            // body-context completions are SSMS's job.
            if (atMatch.Success && !DeclareLine.IsMatch(textBefore))
                AddVariablesDefinedAbove(snapshot, line.LineNumber, completions);
        }
        else
        {
            // Header context.
            bool declareOnLine = DeclareLine.IsMatch(textBefore);

            if (atMatch.Success)
            {
                // ⊕ Sample-data is added FIRST so it's the top hit when the
                // user is sitting under a `Declare @Params_X table (...)`
                // and types @.
                AddSampleDataCompletion(snapshot, line.LineNumber, completions);
                AddHeaderVariables(completions, prependDeclare: !declareOnLine);
                AddVariablesDefinedAbove(snapshot, line.LineNumber, completions);
            }
            else if (DeclareEmptyTail.IsMatch(textBefore))
            {
                AddHeaderVariables(completions, prependDeclare: false);
            }
            else if (BlankOrSqPrefix.IsMatch(textBefore))
            {
                AddFileSnippets(completions);
            }
            // Other header contexts (Declare @x | typing, AS, WITH() ): defer
            // to SSMS's native SQL completion for types and table hints —
            // that list is larger and more authoritative than ours, and
            // matches the user's expectation of "augmentation, not
            // replacement".
        }

        if (completions.Count == 0) return;

        completionSets.Add(new CompletionSet(
            moniker:       "SQuiL",
            displayName:   "SQuiL",
            applicableTo:  applicableTo,
            completions:   completions,
            completionBuilders: null));
    }

    public void Dispose() => _disposed = true;

    // ── Context helpers ────────────────────────────────────────────────

    /// <summary>
    /// "Header" = before the USE line, or the entire file if there is no
    /// USE line yet.  Matches isInHeader() in completionProvider.ts.
    /// </summary>
    private bool IsInHeader(ITextSnapshot snapshot, int caretLine)
    {
        for (int i = 0; i < snapshot.LineCount; i++)
        {
            if (UseLine.IsMatch(snapshot.GetLineFromLineNumber(i).GetText()))
                return caretLine < i;
        }
        return true;
    }

    /// <summary>
    /// Build the tracking span that the completion session will replace.
    /// When the user typed <c>@</c>, replace from the <c>@</c> onward.
    /// Otherwise replace the trailing identifier (or just insert at caret).
    /// </summary>
    private ITrackingSpan ComputeApplicableTo(
        ITextSnapshot snapshot, int caret, Match atMatch, string lineText, int lineStart)
    {
        if (atMatch.Success)
        {
            int start = lineStart + (caret - lineStart) - atMatch.Length;
            return snapshot.CreateTrackingSpan(start, atMatch.Length, SpanTrackingMode.EdgeInclusive);
        }

        // Backtrack across identifier chars to get the existing prefix
        int back = caret;
        while (back > lineStart && IsIdentChar(snapshot[back - 1]))
            back--;
        return snapshot.CreateTrackingSpan(back, caret - back, SpanTrackingMode.EdgeInclusive);
    }

    private static bool IsIdentChar(char c) =>
        c == '_' || char.IsLetterOrDigit(c);

    // ── Completion adders ─────────────────────────────────────────────

    private void AddHeaderVariables(List<EditorCompletion> list, bool prependDeclare)
    {
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupVariable, StandardGlyphItem.GlyphItemPublic);

        foreach (var v in SQuiLCompletionData.HeaderVars)
        {
            string insertion = prependDeclare
                ? $"Declare {v.Insertion};"
                : $"{v.Insertion};";

            list.Add(new EditorCompletion(
                displayText:        v.Prefix,
                insertionText:      insertion,
                description:        $"{v.Detail}\r\n\r\n{v.Documentation}",
                iconSource:         glyph,
                iconAutomationText: "SQuiL variable"));
        }
    }

    private void AddVariablesDefinedAbove(ITextSnapshot snapshot, int caretLine, List<EditorCompletion> list)
    {
        var parsed = SQuiLParser.Parse(snapshot.GetText());
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupVariable, StandardGlyphItem.GlyphItemPublic);

        foreach (var v in parsed.Variables)
        {
            if (v.Line >= caretLine) continue;

            string columns = v.Columns is { Count: > 0 }
                ? "\r\nColumns: " + string.Join(", ", v.Columns.Select(c => c.Name))
                : "";

            list.Add(new EditorCompletion(
                displayText:        v.RawName,
                insertionText:      v.RawName,
                description:        $"{SQuiLParser.DescribeRole(v.Role)}  —  {v.SqlType}{columns}",
                iconSource:         glyph,
                iconAutomationText: "SQuiL declared variable"));
        }
    }

    private void AddSqlTypes(List<EditorCompletion> list)
    {
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupValueType, StandardGlyphItem.GlyphItemPublic);

        foreach (string t in SQuiLCompletionData.SqlTypes)
        {
            list.Add(new EditorCompletion(
                displayText:        t,
                insertionText:      t,
                description:        "SQL type",
                iconSource:         glyph,
                iconAutomationText: "SQL type"));
        }

        list.Add(new EditorCompletion(
            displayText:        "table (...)",
            insertionText:      "table (ColumnName int)",
            description:        "SQL table type",
            iconSource:         glyph,
            iconAutomationText: "SQL table type"));
    }

    private void AddTableHints(List<EditorCompletion> list)
    {
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupConstant, StandardGlyphItem.GlyphItemPublic);

        foreach (string h in SQuiLCompletionData.TableHints)
        {
            list.Add(new EditorCompletion(
                displayText:        h,
                insertionText:      h,
                description:        "SQL table hint",
                iconSource:         glyph,
                iconAutomationText: "SQL table hint"));
        }
    }

    private void AddFileSnippets(List<EditorCompletion> list)
    {
        // StandardGlyphGroup has no "snippet" value — Macro reads as a stamped
        // template, which is the closest semantic match in the built-in set.
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupMacro, StandardGlyphItem.GlyphItemPublic);

        foreach (var s in SQuiLCompletionData.FileSnippets)
        {
            list.Add(new EditorCompletion(
                displayText:        s.Label,
                insertionText:      s.Insertion,
                description:        s.Detail,
                iconSource:         glyph,
                iconAutomationText: "SQuiL snippet"));
        }
    }

    /// <summary>
    /// Offers a "⊕ Insert/Modify sample data → @Var" item when the variable
    /// declared on the line immediately above is a TABLE-typed input
    /// (<c>@Params_</c> list or <c>@Param_ table</c> object).  Selecting the
    /// item is intercepted by <see cref="SQuiLCompletionCommandFilter"/>,
    /// which dispatches to <see cref="Commands.InsertSampleDataCommand"/>
    /// instead of letting the editor insert the (empty) insertion text.
    ///
    /// Mirrors <c>sampleDataCompletions</c> in completionProvider.ts.
    /// </summary>
    private void AddSampleDataCompletion(ITextSnapshot snapshot, int caretLine, List<EditorCompletion> list)
    {
        var parsed = SQuiLParser.Parse(snapshot.GetText());

        // Only the immediately-previous variable matters — not "any var above".
        SQuiLVariable? lastVar = null;
        foreach (var v in parsed.Variables)
        {
            if (v.Line < caretLine) lastVar = v;
        }
        if (lastVar is null) return;
        if (lastVar.Role is not VariableRole.Params and not VariableRole.ParamTable) return;
        if (lastVar.Columns is null || lastVar.Columns.Count == 0) return;

        bool hasBlock = SampleDataGenerator.Exists(snapshot.GetText(), lastVar.RawName);

        // DisplayText MUST start with `@` (or whatever the user typed) — the
        // IntelliSense filter narrows the dropdown on a prefix match against
        // DisplayText, so labels starting with `⊕` get filtered OUT the
        // moment the user types `@`.  We lead with the variable name and
        // put the ⊕ + verb in the middle as an action hint.
        string verb = hasBlock ? "modify" : "insert";
        string label = $"{lastVar.RawName}    ⊕ {verb} sample data";

        string columnList = string.Join(", ", lastVar.Columns.ConvertAll(c => c.Name));
        string description = hasBlock
            ? $"Change the number of test rows for {lastVar.RawName}.\r\n"
            + "Sample data is for local testing only — remove before committing."
            : $"Add test rows to {lastVar.RawName} ({columnList}).\r\n"
            + "Sample data is for local testing only — remove before committing.";

        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupMacro, StandardGlyphItem.GlyphItemPublic);

        list.Add(new SampleDataCompletion(label, description, glyph, lastVar, hasBlock));
    }

    private void AddSqlKeywords(List<EditorCompletion> list)
    {
        // StandardGlyphGroup has no "keyword" value — Intrinsic is the
        // conventional fallback that other VS extensions use for SQL keywords.
        var glyph = _provider.GlyphService.GetGlyph(
            StandardGlyphGroup.GlyphGroupIntrinsic, StandardGlyphItem.GlyphItemPublic);

        foreach (string kw in SQuiLCompletionData.DmlKeywords)
        {
            list.Add(new EditorCompletion(
                displayText:        kw,
                insertionText:      kw,
                description:        "SQL keyword",
                iconSource:         glyph,
                iconAutomationText: "SQL keyword"));
        }
        foreach (string kw in SQuiLCompletionData.ControlKeywords)
        {
            list.Add(new EditorCompletion(
                displayText:        kw,
                insertionText:      kw,
                description:        "Control-flow keyword",
                iconSource:         glyph,
                iconAutomationText: "Control keyword"));
        }
    }
}
