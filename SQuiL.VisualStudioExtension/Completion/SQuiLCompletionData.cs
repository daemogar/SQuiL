using System.Collections.Generic;

namespace SQuiL.VisualStudioExtension.Completion;

/// <summary>
/// Static keyword / type / snippet tables that drive
/// <see cref="SQuiLCompletionSource"/>.  Mirrors the lists in
/// <c>completionProvider.ts</c> so SSMS suggests the same set of identifiers
/// VS Code does.
/// </summary>
internal static class SQuiLCompletionData
{
    /// <summary>
    /// Data-Manipulation keywords offered in body context.  Multi-word entries
    /// (e.g. "Group By") are emitted as-is — VS does not normally tokenise
    /// across a space, but accepting the completion still inserts the whole
    /// phrase.
    /// </summary>
    public static readonly string[] DmlKeywords =
    {
        "Select", "Insert", "Update", "Delete", "Merge", "Truncate",
        "From", "Where",
        "Join", "Inner Join", "Left Join", "Right Join", "Full Outer Join", "Cross Join",
        "On", "Into", "Values", "Set", "Top", "Distinct", "As",
        "Union", "Union All", "Intersect", "Except",
        "Group By", "Order By", "Having", "Over", "Partition By",
        "Rows Between", "Range Between", "Between",
        "And", "Or", "Not", "In", "Like",
        "Is Null", "Is Not Null", "Exists",
        "Case", "When", "Then", "Else", "End",
        "With", "Exec", "Execute", "Output",
        "Declare", "Use",
    };

    public static readonly string[] ControlKeywords =
    {
        "If", "Else", "Begin", "End",
        "While", "Break", "Continue", "Return",
        "RaiseError", "Throw", "Try", "Catch", "Print",
    };

    /// <summary>
    /// SQL types — bare names plus a handful of common parameterised variants
    /// that get used a lot in SQuiL DECLAREs.
    /// </summary>
    public static readonly string[] SqlTypes =
    {
        "bigint", "binary", "bit", "char", "date",
        "datetime", "datetime2", "datetimeoffset",
        "decimal", "float", "image", "int", "money",
        "nchar", "ntext", "numeric", "nvarchar",
        "real", "smalldatetime", "smallint", "smallmoney",
        "text", "time", "tinyint", "uniqueidentifier",
        "varbinary", "varchar", "xml",
        "varchar(50)", "varchar(100)", "varchar(255)", "varchar(max)",
        "nvarchar(50)", "nvarchar(100)", "nvarchar(255)", "nvarchar(max)",
        "decimal(18, 2)", "decimal(18, 4)",
        "char(1)", "char(10)",
    };

    public static readonly string[] TableHints =
    {
        "NoLock", "ReadPast", "UpdLock", "RowLock", "TabLock",
    };

    // ── SQuiL @-prefix declarations ────────────────────────────────────

    public sealed class HeaderVar
    {
        public string Prefix { get; }
        /// <summary>Body text to insert (no trailing semicolon — caller adds one).</summary>
        public string Insertion { get; }
        public string Detail { get; }
        public string Documentation { get; }

        public HeaderVar(string prefix, string insertion, string detail, string documentation)
        {
            Prefix        = prefix;
            Insertion     = insertion;
            Detail        = detail;
            Documentation = documentation;
        }
    }

    /// <summary>
    /// Each entry pairs a user-facing prefix ("@Param_") with concrete text to
    /// insert when accepted.  No <c>${1:placeholder}</c> snippet syntax —
    /// SSMS doesn't host VS Code's snippet engine, so we insert plain text and
    /// rely on the user to overwrite the example fragments.
    /// </summary>
    /// <summary>
    /// Display text is just the prefix the user types — typing @P narrows
    /// the list to entries starting with @P, which only works when the
    /// prefix is at the start of DisplayText.  The role / Request-Response
    /// mapping lives in <see cref="HeaderVar.Detail"/> and surfaces in the
    /// IntelliSense side panel when the entry is highlighted.
    /// </summary>
    public static readonly HeaderVar[] HeaderVars =
    {
        new("@Param_",          "@Param_Name varchar(100)",
            "Input scalar — property on *Request",
            "Maps to a property on the generated *Request record."),

        new("@Params_",         "@Params_Items table (ID int)",
            "Input list — IEnumerable<T> on *Request",
            "Maps to an IEnumerable<ItemT> property on *Request."),

        new("@Return_",         "@Return_Name int",
            "Output scalar — property on *Response",
            "Maps to a property on the generated *Response record."),

        new("@Returns_",        "@Returns_Items table (ID int, Name varchar(100))",
            "Output list — IEnumerable<T> on *Response",
            "Maps to an IEnumerable<ItemT> property on *Response."),

        new("@Debug",           "@Debug bit = 1",
            "Debug flag — always on *Request",
            "*Request always exposes bool Debug + bool DebugOnly. "
          + "Declare in SQL only when the query body needs to read it. "
          + "Default = 1 is convenient when running directly in SSMS."),

        new("@EnvironmentName", "@EnvironmentName varchar(50)",
            "Environment name — resolved by SQuiLBaseDataContext",
            "Resolved from IConfiguration[\"EnvironmentName\"] or ASPNETCORE_ENVIRONMENT "
          + "(defaulting to \"Development\"). Declare only when the body reads it."),

        new("@Error",           "@Error varchar(max)",
            "Error string — appears on *Response when declared",
            "Populated on the response when the query produces an error."),

        new("@Errors",          "@Errors table (Message varchar(max))",
            "Error rows — appears on *Response when declared",
            "Populated on the response with structured error rows."),
    };

    // ── File-level scaffold snippets ──────────────────────────────────

    public sealed class FileSnippet
    {
        public string Label { get; }
        public string Insertion { get; }
        public string Detail { get; }

        public FileSnippet(string label, string insertion, string detail)
        {
            Label     = label;
            Insertion = insertion;
            Detail    = detail;
        }
    }

    /// <summary>
    /// Snippets surface on a blank header line.  Display text reads as a
    /// natural-language action; the first word ("Scaffold", "Declare") is
    /// what the user types to filter the list.
    /// </summary>
    public static readonly FileSnippet[] FileSnippets =
    {
        new("Scaffold a complete SQuiL file",
            string.Join("\r\n", new[]
            {
                "--Name: QueryName",
                "",
                "Declare @Param_Name varchar(100);",
                "Declare @Return_Result int;",
                "",
                "Use [DatabaseName];",
                "",
                "-- SQL body",
                "Set @Return_Result = (Select Count(*) From TableName Where 1=1);",
                "Select @Return_Result;",
            }),
            "Inserts a fully-formed SQuiL file with header, USE, and a sample query body."),

        new("Declare @Param_ input scalar",
            "Declare @Param_Name varchar(100);",
            "Single-value input — becomes a property on *Request."),

        new("Declare @Params_ input list",
            string.Join("\r\n", new[]
            {
                "Declare @Params_Items table (",
                "    ID int",
                ");",
            }),
            "Multi-row input — becomes IEnumerable<T> on *Request."),

        new("Declare @Return_ output scalar",
            "Declare @Return_Name int;",
            "Single-value output — becomes a property on *Response."),

        new("Declare @Returns_ output list",
            string.Join("\r\n", new[]
            {
                "Declare @Returns_Items table (",
                "    ID int,",
                "    Name varchar(100)",
                ");",
            }),
            "Multi-row output — becomes IEnumerable<T> on *Response."),
    };
}
