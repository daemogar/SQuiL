using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;

namespace SQuiL.VisualStudioExtension.Classification;

/// <summary>
/// Line-oriented SQuiL classifier.  Mirrors the scopes in
/// <c>SQuiL.Editor.Shared\squil.tmLanguage.json</c> so SSMS colouring matches
/// VS Code: SQuiL roles (@Param_*, @Return_*, @Debug, …) are coloured
/// distinctly from plain SQL keywords, and the USE statement gets its own
/// scope so the database name reads as an entity rather than a keyword.
///
/// The classifier is intentionally not whole-document re-entrant — block
/// comments are handled per-line by tracking the "inside block comment" state
/// across <see cref="GetClassificationSpans"/> calls for sequential lines.
/// In practice SSMS asks for spans line-by-line top-to-bottom, so this
/// simplification is safe and avoids re-tokenising the buffer on every keystroke.
/// </summary>
internal sealed class SQuiLClassifier : IClassifier
{
    // Required by the IClassifier contract but our classifier is stateless
    // beyond the buffer it was created for, so we never raise it.
    public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged
    {
        add { } remove { }
    }

    private readonly ITextBuffer _buffer;
    private readonly Dictionary<string, IClassificationType> _types;

    // Tracks the start offset of an open /* … */ region that began on an
    // earlier line.  Cleared when we step backwards or to a non-adjacent line.
    private int _blockCommentStart = -1;
    private int _lastLineNumberSeen = -1;

    public SQuiLClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
    {
        _buffer = buffer;

        _types = new Dictionary<string, IClassificationType>
        {
            [SQuiLClassificationTypes.SQuiLParamVariable]    = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLParamVariable),
            [SQuiLClassificationTypes.SQuiLReturnVariable]   = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLReturnVariable),
            [SQuiLClassificationTypes.SQuiLSpecialVariable]  = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLSpecialVariable),
            [SQuiLClassificationTypes.SQuiLOtherVariable]    = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLOtherVariable),
            [SQuiLClassificationTypes.SQuiLUseKeyword]       = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLUseKeyword),
            [SQuiLClassificationTypes.SQuiLDatabaseName]     = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLDatabaseName),
            [SQuiLClassificationTypes.SQuiLDeclareKeyword]   = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLDeclareKeyword),
            [SQuiLClassificationTypes.SQuiLDmlKeyword]       = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLDmlKeyword),
            [SQuiLClassificationTypes.SQuiLDdlKeyword]       = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLDdlKeyword),
            [SQuiLClassificationTypes.SQuiLControlKeyword]   = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLControlKeyword),
            [SQuiLClassificationTypes.SQuiLFunctionKeyword]  = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLFunctionKeyword),
            [SQuiLClassificationTypes.SQuiLSqlType]          = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLSqlType),
            [SQuiLClassificationTypes.SQuiLConstant]         = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLConstant),
            [SQuiLClassificationTypes.SQuiLOperator]         = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLOperator),
            [SQuiLClassificationTypes.SQuiLBracketId]        = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLBracketId),
            [SQuiLClassificationTypes.SQuiLNameAnnotation]   = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLNameAnnotation),
            [SQuiLClassificationTypes.SQuiLLineComment]      = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLLineComment),
            [SQuiLClassificationTypes.SQuiLBlockComment]     = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLBlockComment),
            [SQuiLClassificationTypes.SQuiLString]           = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLString),
            [SQuiLClassificationTypes.SQuiLNumber]           = registry.GetClassificationType(SQuiLClassificationTypes.SQuiLNumber),
        };
    }

    // ── Keyword tables (copied from squil.tmLanguage.json so colouring stays
    //    in lockstep with the VS Code grammar; if you add a keyword there,
    //    add it here too). ────────────────────────────────────────────────

    private static readonly HashSet<string> DmlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT","INSERT","UPDATE","DELETE","MERGE","TRUNCATE","EXEC","EXECUTE",
        "FROM","WHERE","JOIN","INNER","OUTER","LEFT","RIGHT","FULL","CROSS","ON",
        "INTO","VALUES","SET","TOP","DISTINCT","AS","UNION","INTERSECT","EXCEPT","ALL",
        "GROUP","BY","ORDER","HAVING","OVER","PARTITION","ROWS","RANGE","BETWEEN",
        "AND","OR","NOT","IN","LIKE","IS","NULL","EXISTS","ANY","SOME",
        "COALESCE","NULLIF","CASE","WHEN","THEN","ELSE","END",
        "CAST","CONVERT","OUTPUT","RETURNING","WITH",
        "NOLOCK","READPAST","UPDLOCK","ROWLOCK","TABLOCK","HOLDLOCK","NOEXPAND",
        "READCOMMITTED","READUNCOMMITTED","SERIALIZABLE","SNAPSHOT","REPEATABLE","READ",
    };

    private static readonly HashSet<string> DdlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "CREATE","ALTER","DROP","TABLE","VIEW","INDEX","PROCEDURE","FUNCTION","TRIGGER",
        "DATABASE","SCHEMA","CONSTRAINT","PRIMARY","KEY","FOREIGN","UNIQUE","DEFAULT",
        "CHECK","REFERENCES","IDENTITY","ROWGUIDCOL","SPARSE","FILESTREAM","PERSISTED","COMPUTED",
    };

    private static readonly HashSet<string> ControlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "IF","ELSE","BEGIN","END","WHILE","BREAK","CONTINUE","RETURN","GOTO",
        "RAISERROR","THROW","TRY","CATCH","PRINT","WAITFOR","DELAY","TIME",
    };

    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT","SUM","AVG","MIN","MAX","ABS","CEILING","FLOOR","ROUND","SQRT","POWER",
        "LOG","EXP","SIGN","RAND","PI","LEN","LEFT","RIGHT","SUBSTRING","CHARINDEX","PATINDEX",
        "REPLACE","REPLICATE","REVERSE","LTRIM","RTRIM","TRIM","UPPER","LOWER","STR","STUFF",
        "FORMAT","CONCAT","ISNULL","ISNUMERIC","ISDATE","NEWID","GETDATE","GETUTCDATE",
        "SYSDATETIME","DATEPART","DATEDIFF","DATEADD","EOMONTH","YEAR","MONTH","DAY",
        "TRY_CAST","TRY_CONVERT","PARSE","TRY_PARSE","ROW_NUMBER","RANK","DENSE_RANK","NTILE",
        "LAG","LEAD","FIRST_VALUE","LAST_VALUE","CUME_DIST","PERCENT_RANK",
        "PERCENTILE_CONT","PERCENTILE_DISC","SCOPE_IDENTITY",
        "OBJECT_ID","OBJECT_NAME","SCHEMA_ID","SCHEMA_NAME","TYPE_ID","TYPE_NAME",
    };

    private static readonly HashSet<string> SqlTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BIGINT","BINARY","BIT","CHAR","DATE","DATETIME","DATETIME2","DATETIMEOFFSET",
        "DECIMAL","FLOAT","IMAGE","INT","MONEY","NCHAR","NTEXT","NUMERIC","NVARCHAR",
        "REAL","SMALLDATETIME","SMALLINT","SMALLMONEY","SQL_VARIANT","SYSNAME","TEXT",
        "TIME","TIMESTAMP","TINYINT","UNIQUEIDENTIFIER","VARBINARY","VARCHAR","XML",
        "TABLE","MAX",
    };

    private static readonly HashSet<string> Constants = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRUE","FALSE","NULL",
    };

    // ── Regexes used for tokenization.  Anchored to the line scope; multiline
    //    handling is layered on top of the per-line scan. ──────────────────

    // --Name: QueryName  (only meaningful at top, but recognise anywhere)
    private static readonly Regex NameAnnotation = new(
        @"^(--)\s*(Name:)\s*(\S.*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // -- … line comment
    private static readonly Regex LineComment = new(
        @"--.*$",
        RegexOptions.Compiled);

    // 'string' (single-quoted, doubled '' = escape)
    private static readonly Regex StringLiteral = new(
        @"'(?:[^']|'')*'",
        RegexOptions.Compiled);

    // 123  123.45  1e10
    private static readonly Regex NumberLiteral = new(
        @"\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b",
        RegexOptions.Compiled);

    // [Bracketed Identifier]
    private static readonly Regex BracketIdentifier = new(
        @"\[[^\]\r\n]+\]",
        RegexOptions.Compiled);

    // USE [DbName]  or  USE DbName
    private static readonly Regex UseStatement = new(
        @"\b(USE)\b\s+(\[?)(\w+)(\]?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // @Variable  ( captures the @-prefixed identifier whole )
    private static readonly Regex AtVariable = new(
        @"@\w+",
        RegexOptions.Compiled);

    // identifier or word (used to look up keyword tables)
    private static readonly Regex Identifier = new(
        @"\b[A-Za-z_]\w*\b",
        RegexOptions.Compiled);

    // <>  !=  >=  <=  >  <  =  +  -  *  /  %  ||  ;  ,
    private static readonly Regex Operator = new(
        @"<>|!=|>=|<=|>|<|=|\+|-|\*|/|%|\|\||;|,",
        RegexOptions.Compiled);

    // ──────────────────────────────────────────────────────────────────────

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
    {
        var results = new List<ClassificationSpan>();
        var snapshot = span.Snapshot;
        var line = snapshot.GetLineFromPosition(span.Start.Position);
        var endLine = snapshot.GetLineFromPosition(span.End.Position);

        // Crude block-comment state reset when SSMS asks for non-sequential
        // lines (most commonly: redraw of a single edited line).  This avoids
        // a stale "inside block comment" state from leaking across edits.
        if (line.LineNumber != _lastLineNumberSeen + 1)
            _blockCommentStart = -1;

        for (int ln = line.LineNumber; ln <= endLine.LineNumber; ln++)
        {
            var l = snapshot.GetLineFromLineNumber(ln);
            ClassifyLine(l, results);
            _lastLineNumberSeen = ln;
        }

        return results;
    }

    private void ClassifyLine(ITextSnapshotLine snapLine, List<ClassificationSpan> results)
    {
        var text = snapLine.GetText();
        int lineStart = snapLine.Start.Position;
        var taken = new bool[text.Length]; // marks chars already coloured

        // ── 1. Continued block comment ───────────────────────────────────
        if (_blockCommentStart >= 0)
        {
            int closeIdx = text.IndexOf("*/", StringComparison.Ordinal);
            if (closeIdx < 0)
            {
                Add(results, snapLine.Snapshot, lineStart, text.Length, SQuiLClassificationTypes.SQuiLBlockComment);
                Mark(taken, 0, text.Length);
                return;
            }
            else
            {
                Add(results, snapLine.Snapshot, lineStart, closeIdx + 2, SQuiLClassificationTypes.SQuiLBlockComment);
                Mark(taken, 0, closeIdx + 2);
                _blockCommentStart = -1;
            }
        }

        // ── 2. Block comments that open on this line ─────────────────────
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (taken[i]) continue;
            if (text[i] == '/' && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    Add(results, snapLine.Snapshot, lineStart + i, text.Length - i, SQuiLClassificationTypes.SQuiLBlockComment);
                    Mark(taken, i, text.Length - i);
                    _blockCommentStart = lineStart + i;
                    return;
                }
                else
                {
                    int len = close + 2 - i;
                    Add(results, snapLine.Snapshot, lineStart + i, len, SQuiLClassificationTypes.SQuiLBlockComment);
                    Mark(taken, i, len);
                    i = close + 1;
                }
            }
        }

        // ── 3. --Name: annotation (must precede generic line-comment) ────
        var annot = NameAnnotation.Match(text);
        if (annot.Success && IsFree(taken, annot.Index, annot.Length))
        {
            Add(results, snapLine.Snapshot, lineStart + annot.Index, annot.Length, SQuiLClassificationTypes.SQuiLNameAnnotation);
            Mark(taken, annot.Index, annot.Length);
        }

        // ── 4. line comments ─────────────────────────────────────────────
        foreach (Match m in LineComment.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, SQuiLClassificationTypes.SQuiLLineComment);
            Mark(taken, m.Index, m.Length);
        }

        // ── 5. strings ───────────────────────────────────────────────────
        foreach (Match m in StringLiteral.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, SQuiLClassificationTypes.SQuiLString);
            Mark(taken, m.Index, m.Length);
        }

        // ── 6. USE statement (keyword + db name) ─────────────────────────
        foreach (Match m in UseStatement.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            var kw   = m.Groups[1];
            var dbId = m.Groups[3];
            Add(results, snapLine.Snapshot, lineStart + kw.Index,   kw.Length,   SQuiLClassificationTypes.SQuiLUseKeyword);
            Add(results, snapLine.Snapshot, lineStart + dbId.Index, dbId.Length, SQuiLClassificationTypes.SQuiLDatabaseName);
            Mark(taken, kw.Index,   kw.Length);
            Mark(taken, dbId.Index, dbId.Length);
        }

        // ── 7. bracket identifiers ───────────────────────────────────────
        foreach (Match m in BracketIdentifier.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, SQuiLClassificationTypes.SQuiLBracketId);
            Mark(taken, m.Index, m.Length);
        }

        // ── 8. @-prefixed variables (classified by role) ─────────────────
        foreach (Match m in AtVariable.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            var role = ClassifyAtVariable(m.Value);
            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, role);
            Mark(taken, m.Index, m.Length);
        }

        // ── 9. numbers ───────────────────────────────────────────────────
        foreach (Match m in NumberLiteral.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, SQuiLClassificationTypes.SQuiLNumber);
            Mark(taken, m.Index, m.Length);
        }

        // ── 10. SQuiL-specific identifiers only ─────────────────────────
        // Plain SQL keywords / types / functions are intentionally NOT
        // classified here — SSMS's built-in SQL classifier already colours
        // SELECT / FROM / int / varchar / NEWID() etc., and adding our own
        // tags on top produced a colour collision.  We only emit tags for
        // identifiers SQuiL adds meaning to: the DECLARE keyword (because
        // SQuiL's parser keys off it) and TRUE/FALSE/NULL constants (no
        // semantic reason — kept for parity with the tmLanguage scopes).
        foreach (Match m in Identifier.Matches(text))
        {
            if (!IsFree(taken, m.Index, m.Length)) continue;
            var word = m.Value;
            string? type = LookupSquilIdentifier(word);
            if (type == null) continue;

            Add(results, snapLine.Snapshot, lineStart + m.Index, m.Length, type);
            Mark(taken, m.Index, m.Length);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ClassifyAtVariable(string raw)
    {
        // Match against parser.ts ClassifyAtVariable in SQuiL.VSCodeExtension
        // so a variable that lights up green in VS Code lights up green in SSMS.
        var upper = raw.ToUpperInvariant();
        if (upper == "@DEBUG" || upper == "@ENVIRONMENTNAME" || upper == "@ERROR" || upper == "@ERRORS")
            return SQuiLClassificationTypes.SQuiLSpecialVariable;
        if (upper.StartsWith("@PARAMS_") || upper.StartsWith("@PARAM_"))
            return SQuiLClassificationTypes.SQuiLParamVariable;
        if (upper.StartsWith("@RETURNS_") || upper.StartsWith("@RETURN_"))
            return SQuiLClassificationTypes.SQuiLReturnVariable;
        return SQuiLClassificationTypes.SQuiLOtherVariable;
    }

    private static string? LookupSquilIdentifier(string word)
    {
        // Only SQuiL-distinct identifiers are tagged here.  Generic SQL
        // keywords (SELECT/FROM/WHERE/…) fall through and pick up SSMS's
        // native classification.
        //
        // Exception: SQL TYPES (int / varchar / nvarchar / …) DO get our
        // own classification, so the user can colour them distinctly from
        // SSMS's "keyword" blue.  SSMS lumps types and keywords together
        // by default; users have asked for them to be visually different.
        if (word.Equals("DECLARE", StringComparison.OrdinalIgnoreCase))
            return SQuiLClassificationTypes.SQuiLDeclareKeyword;
        // USE is handled by the dedicated UseStatement regex (kw + db name).
        if (word.Equals("USE", StringComparison.OrdinalIgnoreCase))
            return null;

        if (SqlTypes.Contains(word))
            return SQuiLClassificationTypes.SQuiLSqlType;
        return null;
    }

    private static bool IsFree(bool[] taken, int start, int length)
    {
        int end = Math.Min(taken.Length, start + length);
        for (int i = Math.Max(0, start); i < end; i++)
            if (taken[i]) return false;
        return true;
    }

    private static void Mark(bool[] taken, int start, int length)
    {
        int end = Math.Min(taken.Length, start + length);
        for (int i = Math.Max(0, start); i < end; i++)
            taken[i] = true;
    }

    private void Add(List<ClassificationSpan> results, ITextSnapshot snapshot, int start, int length, string type)
    {
        if (length <= 0) return;
        if (!_types.TryGetValue(type, out var classification) || classification == null) return;
        results.Add(new ClassificationSpan(new SnapshotSpan(snapshot, start, length), classification));
    }
}
