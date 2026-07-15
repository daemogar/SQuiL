using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using SQuiL.VisualStudioExtension.Parsing;

namespace SQuiL.VisualStudioExtension.QuickInfo;

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
        if (atSpan != null)
        {
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

        return GetColumnLinkRoleQuickInfoItemAsync(snapshot, triggerPoint.Value.Position);
    }

    /// <summary>
    /// Hover for a bare table-column identifier (no <c>@</c> prefix) that
    /// plays a role in the nested-object PK/FK-by-convention graph — see
    /// <see cref="SQuiLLinter.DescribeColumnLinkRole"/>. Columns that play no
    /// link role fall through to null, leaving hover completely unchanged
    /// (graceful degradation — a no-links file shows no link text at all).
    /// Ported to hoverProvider.ts's <c>provideColumnLinkRoleHover</c> — change
    /// one side, change all three.
    /// </summary>
    private static Task<QuickInfoItem?> GetColumnLinkRoleQuickInfoItemAsync(ITextSnapshot snapshot, int position)
    {
        var identSpan = TryGetIdentifierSpan(snapshot, position);
        if (identSpan == null) return Task.FromResult<QuickInfoItem?>(null);

        var line = snapshot.GetLineFromPosition(identSpan.Value.Start.Position);
        int character = identSpan.Value.Start.Position - line.Start.Position;

        var parsed = SQuiLParser.Parse(snapshot.GetText());
        string? roleText = SQuiLLinter.DescribeColumnLinkRole(parsed, line.LineNumber, character);
        if (roleText == null) return Task.FromResult<QuickInfoItem?>(null);

        var trackingSpan = snapshot.CreateTrackingSpan(identSpan.Value, SpanTrackingMode.EdgeInclusive);
        var content = new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, roleText));

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

    /// <summary>
    /// Returns the span of the bare identifier (no leading <c>@</c> required)
    /// containing <paramref name="position"/>, or null if the position is not
    /// on one. Used to resolve table-column-name hovers (nested-object link
    /// roles), which never carry an <c>@</c> prefix.
    /// </summary>
    private static SnapshotSpan? TryGetIdentifierSpan(ITextSnapshot snapshot, int position)
    {
        if (position < 0 || position > snapshot.Length) return null;

        int left = position;
        while (left > 0 && IsIdentChar(snapshot[left - 1])) left--;

        int right = position;
        while (right < snapshot.Length && IsIdentChar(snapshot[right])) right++;

        if (right <= left) return null;
        return new SnapshotSpan(snapshot, left, right - left);
    }

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
              + "  @Debug, @SuppressDebug, @EnvironmentName, @AsOfDate  special variables"));

        return new ContainerElement(ContainerElementStyle.Stacked, header, hint);
    }

    private static ContainerElement BuildVariableContent(SQuiLVariable v)
    {
        var header = new ClassifiedTextElement(
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Identifier, v.RawName),
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Other, "  —  "),
            new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment, SQuiLParser.DescribeRole(v.Role)));

        // @AsOfDate is a special only in recognition — unlike the other specials
        // it IS emitted as a nullable typed property on *Request, so it gets the
        // full type details below (with a nullable note) rather than the
        // "not emitted as a C# property" message.
        if (v.Role == VariableRole.AsOfDate)
        {
            // Map only the type token (drop any "= default" the SQL initializer adds).
            string asOfType = v.SqlType.Split(new[] { ' ', '=' }, 2)[0];
            var asOfDetails = new ClassifiedTextElement(
                Field("SQL type",     v.SqlType),
                NewLine,
                Field("C# type",      $"{SqlTypeMap.SqlToCSharp(asOfType)}?"),
                NewLine,
                Field("C# name",      v.Name),
                NewLine,
                Field("Generated in", "*Request"));
            var asOfNote = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment,
                    "Special SQuiL variable — emitted as a nullable typed property on *Request. "
                  + "When null, the current time at execution is substituted."));
            return new ContainerElement(ContainerElementStyle.Stacked, header, asOfDetails, asOfNote);
        }

        bool isSpecial = v.Role
            is VariableRole.Debug
            or VariableRole.SuppressDebug
            or VariableRole.EnvironmentName
            or VariableRole.Unknown;

        if (isSpecial)
        {
            // @Debug / @SuppressDebug surface as bool properties on *Request when
            // declared but are not ordinary mapped params; @EnvironmentName is not
            // emitted as a property at all. The shared note keeps the tooltip simple —
            // the role description above carries the precise behavior.
            var note = new ClassifiedTextElement(
                new ClassifiedTextRun(PredefinedClassificationTypeNames.Comment,
                    "Special SQuiL variable — not emitted as an ordinary mapped C# property."));
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
            string recordTypeName = v.Name;

            var sb = new StringBuilder();
            sb.AppendLine($"Columns → {recordTypeName} record:");
            foreach (var col in v.Columns)
            {
                string colCs = SqlTypeMap.SqlToCSharp(col.SqlType);
                bool nullable = col.Nullable;
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
