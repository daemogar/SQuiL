using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using SQuiL.SsmsExtension.Parsing;

namespace SQuiL.SsmsExtension.QuickInfo;

/// <summary>
/// Async QuickInfo source — port of <c>hoverProvider.ts</c>.
///
/// Triggered when the user hovers over an <c>@</c>-prefixed identifier.  We
/// parse the buffer, find the matching <see cref="SQuiLVariable"/> (case-
/// insensitive), and emit a tooltip with:
///   • Role description (matches <c>describeRole</c> in parser.ts)
///   • SQL type, mapped C# type, mapped C# name
///   • Which generated record it ends up on (<c>*Request</c> / <c>*Response</c>)
///   • Column list with C# types when the variable is a TABLE.
///
/// For unknown <c>@</c>-vars we still show a quick reminder of the SQuiL
/// naming conventions so the writer can fix the prefix without leaving the
/// hover.
/// </summary>
internal sealed class SQuiLQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _buffer;
    private bool _disposed;

    public SQuiLQuickInfoSource(ITextBuffer buffer) => _buffer = buffer;

    public void Dispose() => _disposed = true;

    public Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        if (_disposed) return Task.FromResult<QuickInfoItem?>(null);

        var snapshot = _buffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(snapshot);
        if (triggerPoint == null) return Task.FromResult<QuickInfoItem?>(null);

        var atSpan = TryGetAtVariableSpan(snapshot, triggerPoint.Value.Position);
        if (atSpan == null) return Task.FromResult<QuickInfoItem?>(null);

        string word = atSpan.Value.GetText();

        var parsed = SQuiLParser.Parse(snapshot.GetText());
        var variable = parsed.Variables.FirstOrDefault(v =>
            string.Equals(v.RawName, word, StringComparison.OrdinalIgnoreCase));

        var trackingSpan = snapshot.CreateTrackingSpan(atSpan.Value, SpanTrackingMode.EdgeInclusive);

        object content = variable == null
            ? BuildUnknownContent(word)
            : BuildVariableContent(variable);

        return Task.FromResult<QuickInfoItem?>(new QuickInfoItem(trackingSpan, content));
    }

    // ── Word-at-position helper ───────────────────────────────────────────

    /// <summary>
    /// Returns the span of the <c>@identifier</c> that contains
    /// <paramref name="position"/>, or null if the position is not on one.
    /// Mirrors VS Code's <c>getWordRangeAtPosition(/@[\w_]+/)</c>.
    /// </summary>
    private static SnapshotSpan? TryGetAtVariableSpan(ITextSnapshot snapshot, int position)
    {
        if (position < 0 || position > snapshot.Length) return null;

        // Walk left to find the start (@), failing if we hit a non-ident char first.
        int left = position;
        while (left > 0 && IsIdentChar(snapshot[left - 1])) left--;
        if (left == 0 || snapshot[left - 1] != '@') return null;
        int start = left - 1;

        // Walk right across identifier chars.
        int right = position;
        while (right < snapshot.Length && IsIdentChar(snapshot[right])) right++;

        if (right - start <= 1) return null;
        return new SnapshotSpan(snapshot, start, right - start);
    }

    private static bool IsIdentChar(char c) => c == '_' || char.IsLetterOrDigit(c);

    // ── Content builders ──────────────────────────────────────────────────

    private static ContainerElement BuildUnknownContent(string word)
    {
        // Use ContainerElement/ClassifiedTextElement so the tooltip renders
        // with editor-aware styling (light/dark theme aware).
        var header = new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, word),
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Other, "  —  unrecognised variable"));

        var hint = new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment,
                "SQuiL naming conventions:\r\n"
              + "  @Param_Name      input scalar\r\n"
              + "  @Params_Name     input table-valued\r\n"
              + "  @Return_Name     output scalar\r\n"
              + "  @Returns_Name    output table\r\n"
              + "  @Debug, @EnvironmentName, @Error, @Errors  special variables"));

        return new ContainerElement(ContainerElementStyle.Stacked, header, hint);
    }

    private static ContainerElement BuildVariableContent(SQuiLVariable v)
    {
        bool isSpecial = v.Role
            is VariableRole.Debug
            or VariableRole.EnvironmentName
            or VariableRole.Error
            or VariableRole.Errors
            or VariableRole.Unknown;

        var header = new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, v.RawName),
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Other, "  —  "),
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, SQuiLParser.DescribeRole(v.Role)));

        if (isSpecial)
        {
            var note = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment,
                    "Special SQuiL variable — not emitted as a C# property."));
            return new ContainerElement(ContainerElementStyle.Stacked, header, note);
        }

        string csType = SqlTypeMap.GetCSharpType(v);
        string generatedIn = v.Role is VariableRole.Param or VariableRole.Params or VariableRole.ParamTable
            ? "*Request"
            : "*Response";

        var details = new ClassifiedTextElement(
            Field("SQL type",    v.SqlType),
            NewLine,
            Field("C# type",     csType),
            NewLine,
            Field("C# name",     v.Name),
            NewLine,
            Field("Generated in", generatedIn));

        var elements = new ContainerElement(ContainerElementStyle.Stacked, header, details);

        if (v.Columns is { Count: > 0 })
        {
            string recordTypeName = v.Role is VariableRole.Params or VariableRole.Returns
                ? $"{v.Name}Table"
                : $"{v.Name}Object";

            var sb = new StringBuilder();
            sb.AppendLine($"Columns → {recordTypeName} record:");
            foreach (var col in v.Columns)
            {
                string colCs = SqlTypeMap.SqlToCSharp(col.SqlType);
                bool nullable = col.Nullable || SqlTypeMap.IsRefType(col.SqlType);
                string suffix = nullable ? "?" : "";
                sb.AppendLine($"  {colCs}{suffix} {col.Name}");
            }

            var columnsBlock = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, sb.ToString().TrimEnd()));

            return new ContainerElement(ContainerElementStyle.Stacked, header, details, columnsBlock);
        }

        return elements;
    }

    // Small layout helpers — these classified runs render with the editor's
    // theme-aware font/colour mapping rather than as plain literal strings.
    private static ClassifiedTextRun Field(string label, string value) =>
        new(PredefinedClassificationTypeNames.Comment, $"{label}: {value}");

    private static readonly ClassifiedTextRun NewLine =
        new(PredefinedClassificationTypeNames.Other, "\r\n");
}
