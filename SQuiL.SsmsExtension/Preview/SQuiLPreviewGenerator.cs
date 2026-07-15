using System.Collections.Generic;
using System.Linq;
using System.Text;
using SQuiL.SsmsExtension.Parsing;

namespace SQuiL.SsmsExtension.Preview;

/// <summary>
/// Generates a human-readable preview of the C# the SQuiL source generator
/// will emit for a given .squil file.  Port of
/// <c>SQuiL.VSCodeExtension/src/squil/previewGenerator.ts</c>.
///
/// This is a *preview* — not an exact reproduction of generator output.  It
/// captures the structure (records, request/response, data context method,
/// DI hint) so a developer can sanity-check their SQL declarations without
/// running <c>dotnet build</c>.  Run-time output may differ in trivial
/// formatting; for byte-exact code, use the Build SQuiL Project command.
/// </summary>
internal static class SQuiLPreviewGenerator
{
    /// <summary>
    /// Record-type name for a TABLE-valued variable.
    /// The Table/Object suffix was dropped in TODO #3 — the bare name is used directly.
    /// </summary>
    private static string RecordTypeName(SQuiLVariable v) => v.Name;

    private static string GetPropertyType(SQuiLVariable v, string? modelsNs = null)
    {
        if (v.Role is VariableRole.Params or VariableRole.Returns)
        {
            string typeName = modelsNs is not null ? $"{modelsNs}.{RecordTypeName(v)}" : RecordTypeName(v);
            return $"List<{typeName}>?";
        }
        if (v.Role is VariableRole.ParamTable or VariableRole.ReturnTable)
        {
            string typeName = modelsNs is not null ? $"{modelsNs}.{RecordTypeName(v)}" : RecordTypeName(v);
            return $"{typeName}?";
        }

        // Scalars: nullable only when explicitly marked NULL in the SQL declaration.
        string cs = SqlTypeMap.SqlToCSharp(v.SqlType);
        return v.Nullable ? $"{cs}?" : cs;
    }

    private static bool IsCollection(SQuiLVariable v) =>
        v.Role is VariableRole.Params or VariableRole.Returns;

    // ── Nested-objects key graph (preview-only mirror of SQuiLKeyGraph.cs) ──

    /// <summary>Parent → its direct children (declaration order) plus a lookup for "is this
    /// variable someone's child" — the child collapses into the parent record and drops off
    /// the Response top level.</summary>
    private sealed class NestedGraph
    {
        public List<SQuiLVariable> Roots { get; } = new();
        public Dictionary<SQuiLVariable, List<SQuiLVariable>> ChildrenOf { get; } = new();
        private readonly HashSet<SQuiLVariable> _children = new();
        public bool IsChild(SQuiLVariable v) => _children.Contains(v);
        public void MarkChild(SQuiLVariable v) => _children.Add(v);
    }

    /// <summary>
    /// Minimal preview mirror of the generator's <c>SQuiLKeyGraph</c>
    /// (<c>SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs</c>): a table/object
    /// variable's key is its single <c>Primary Key</c> column; any OTHER variable in the
    /// SAME universe carrying a column of that exact name becomes its child. Variables
    /// nobody links to are roots. Called once for OUTPUT (<c>@Return*</c>) table/object
    /// variables and once for INPUT (<c>@Param*</c>) table/object variables (never mixed),
    /// matching the generator building one graph per side (FileGenerator.cs's
    /// <c>keyGraph</c> / <c>inputGraph</c>).
    ///
    /// Simplified relative to the generator: ambiguous (&gt;1 distinct parent) or cyclic
    /// links are build-time errors owned by the generator/editor diagnostics, not the
    /// preview — here the first matching PK owner silently wins so the preview always
    /// renders something reasonable (graceful degradation to the flat shape when there are
    /// no links at all).
    /// </summary>
    private static NestedGraph BuildNestedGraph(List<SQuiLVariable> tableVars)
    {
        var pkOwner = new Dictionary<string, SQuiLVariable>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var v in tableVars)
        {
            var pk = v.Columns?.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is not null && !pkOwner.ContainsKey(pk.Name))
                pkOwner[pk.Name] = v;
        }

        var parentOf = new Dictionary<SQuiLVariable, SQuiLVariable>();
        foreach (var child in tableVars)
        {
            foreach (var col in child.Columns ?? new List<TableColumn>())
            {
                if (!pkOwner.TryGetValue(col.Name, out var owner) || ReferenceEquals(owner, child))
                    continue;
                parentOf[child] = owner;
                break;
            }
        }

        var graph = new NestedGraph();
        foreach (var v in tableVars)
        {
            if (!parentOf.TryGetValue(v, out var parent)) continue;
            graph.MarkChild(v);
            if (!graph.ChildrenOf.TryGetValue(parent, out var list))
                graph.ChildrenOf[parent] = list = new List<SQuiLVariable>();
            list.Add(v);
        }
        graph.Roots.AddRange(tableVars.Where(v => !parentOf.ContainsKey(v)));
        return graph;
    }

    public static string Generate(SQuiLParseResult parsed, string queryName, string ns = "YourNamespace", bool enabled = false, bool debugRollback = true)
    {
        string db = parsed.Database ?? "/* database */";
        var lines = new List<string>();

        var paramVars = parsed.Variables.Where(v =>
            v.Role is VariableRole.Param or VariableRole.Params or VariableRole.ParamTable).ToList();
        var returnVars = parsed.Variables.Where(v =>
            v.Role is VariableRole.Return or VariableRole.Returns or VariableRole.ReturnTable).ToList();

        // Collect all table-valued variables that need row records
        var paramTableVars = paramVars.Where(v => v.Columns is { Count: > 0 }).ToList();
        var returnTableVars = returnVars.Where(v => v.Columns is { Count: > 0 }).ToList();
        var tableVars = paramTableVars.Concat(returnTableVars).ToList();
        // The Namespace override on [SQuiLQuery] is generator-only; editors cannot read C# attributes,
        // so the preview always uses the default "Models" sub-namespace segment.
        string modelsNs = $"{ns}.Models";

        // Nested-objects: OUTPUT and INPUT table/object variables each link into their OWN
        // parent/child graph (never mixed, matching the generator's two independent graphs).
        // Children collapse into their parent record and drop off the Request/Response top level.
        var outputGraph = BuildNestedGraph(returnTableVars);
        var inputGraph = BuildNestedGraph(paramTableVars);
        var responseVars = returnVars.Where(v => !outputGraph.IsChild(v)).ToList();
        var requestVars = paramVars.Where(v => !inputGraph.IsChild(v)).ToList();

        List<SQuiLVariable>? ChildrenOf(SQuiLVariable v) =>
            outputGraph.ChildrenOf.TryGetValue(v, out var oc) ? oc :
            inputGraph.ChildrenOf.TryGetValue(v, out var ic) ? ic : null;

        EmitBanner(lines, queryName, db);
        lines.Add("");
        lines.Add($"namespace {ns};");
        lines.Add("");

        // ── QueryFiles enum hint ────────────────────────────────────────
        lines.Add("// ── QueryFiles enum entry ────────────────────────────────");
        lines.Add("// Generated by SQuiL.SourceGenerator — do not edit manually.");
        lines.Add("// Your QueryFiles enum will include:");
        lines.Add($"//   public enum QueryFiles {{ ..., {queryName} }}");
        lines.Add("");

        // ── using for the Models sub-namespace (only when row records exist)
        if (tableVars.Count > 0)
        {
            lines.Add($"using {modelsNs};");
            lines.Add("");
        }

        // ── Request record (always partial; specials are opt-in). Only nesting ROOTS
        // appear at the top level — an input child collapses into its parent record as
        // a member instead (mirrors the Response nesting below). ──────────────────────
        lines.Add("// ── Request ─────────────────────────────────────────────");
        EmitModelRecord(lines, $"{queryName}Request", requestVars, isResponse: false, parsed.Variables, modelsNs);

        // ── Response record (only nesting ROOTS appear at the top level — a
        // child collapses into its parent record as a member instead) ──────
        if (returnVars.Count > 0)
        {
            lines.Add("// ── Response ────────────────────────────────────────────");
            EmitModelRecord(lines, $"{queryName}Response", responseVars, isResponse: true, modelsNs: modelsNs);
        }

        // ── Data context ────────────────────────────────────────────────
        lines.Add("// ── DataContext ─────────────────────────────────────────");
        lines.Add("// SQuiL emits this method into your partial class. You may omit the");
        lines.Add("// base type and constructor — SQuiL supplies both when absent:");
        lines.Add($"//   public partial class {queryName}DataContext {{ }}");
        lines.Add("// (Add your own constructor to customize; it must call : base(configuration).)");
        lines.Add("");

        string responseType = returnVars.Count == 0
            ? "SQuiLResultType"
            : $"SQuiLResultType<{queryName}Response>";

        lines.Add($"public async Task<{responseType}> Process{queryName}Async(");
        lines.Add($"    {queryName}Request request,");
        lines.Add("    CancellationToken cancellationToken = default!)");
        lines.Add("{");
        if (enabled)
        {
            // Detect @Debug declaration to determine the correct commit gate.
            bool hasDebug = parsed.Variables.Any(v => v.Role == VariableRole.Debug);
            string commitGate = (hasDebug && debugRollback)
                ? "errors.Count == 0 && !__debug"
                : "errors.Count == 0";

            lines.Add("    await connection.OpenAsync(cancellationToken);");
            lines.Add("");
            lines.Add("    using var transaction = connection.BeginTransaction();");
            lines.Add("    command.Transaction = transaction;");
            lines.Add("");
            lines.Add("    /* …read / execute… */");
            lines.Add("");
            lines.Add($"    if ({commitGate})");
            lines.Add("        transaction.Commit();");
            lines.Add("    else");
            lines.Add("        transaction.Rollback();");
        }
        else
        {
            lines.Add("    /* generated body */");
        }
        lines.Add("}");
        lines.Add("");

        // ── DI extension hint ───────────────────────────────────────────
        lines.Add("// ── Dependency Injection ────────────────────────────────");
        lines.Add("// SQuiL emits an AddSQuiL extension that registers every data context:");
        lines.Add("//");
        lines.Add("//   builder.AddSQuiL();");
        lines.Add("//");
        lines.Add($"// Connection string key: \"ConnectionStrings:{db}\"");

        // ── Row records emitted into the .Models sub-namespace
        if (tableVars.Count > 0)
        {
            lines.Add("");
            lines.Add("// ── Row records ─────────────────────────────────────────");
            lines.Add("// Row records live in the .Models sub-namespace, mirroring the generator.");
            lines.Add($"namespace {modelsNs};");
            lines.Add("");
            foreach (var v in tableVars)
                EmitTableRecord(lines, RecordTypeName(v), v, modelsNs, ChildrenOf(v));
        }

        return string.Join("\r\n", lines);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void EmitBanner(List<string> lines, string queryName, string db)
    {
        lines.Add("// ╔═══════════════════════════════════════════════════════╗");
        lines.Add("// ║  SQuiL Generated C# Preview                          ║");
        lines.Add("// ╠═══════════════════════════════════════════════════════╣");
        lines.Add($"// ║  Query    : {Pad(queryName, 41)}║");
        lines.Add($"// ║  Database : {Pad(db, 41)}║");
        lines.Add("// ╠═══════════════════════════════════════════════════════╣");
        lines.Add("// ║  ⚠  This is a PREVIEW only.                          ║");
        lines.Add("// ║     Actual code is emitted by SQuiL.SourceGenerator  ║");
        lines.Add("// ║     when you run  dotnet build.                       ║");
        lines.Add("// ╚═══════════════════════════════════════════════════════╝");
    }

    private static string Pad(string s, int len) =>
        s.Length >= len ? s.Substring(0, len) : s + new string(' ', len - s.Length);

    private static void EmitTableRecord(
        List<string> lines, string typeName, SQuiLVariable v,
        string? modelsNs = null, List<SQuiLVariable>? children = null)
    {
        if (v.Columns is null || v.Columns.Count == 0) return;

        string CsType(TableColumn col)
        {
            string cs = SqlTypeMap.SqlToCSharp(col.SqlType);
            bool nullable = col.Nullable;
            return nullable ? cs + "?" : cs;
        }

        var positional = v.Columns.Where(c => c.DefaultValue is null).ToList();
        var defaulted = v.Columns.Where(c => c.DefaultValue is not null).ToList();
        string @params = string.Join(", ", positional.Select(c => $"{CsType(c)} {c.Name}"));
        bool hasChildren = children is { Count: > 0 };

        if (defaulted.Count == 0 && !hasChildren)
        {
            lines.Add($"public partial record {typeName}({@params});");
            lines.Add("");
            return;
        }

        lines.Add($"public partial record {typeName}({@params})");
        lines.Add("{");
        foreach (var col in defaulted)
            lines.Add($"    public {CsType(col)} {col.Name} {{ get; init; }} = {CSharpDefault(col.SqlType, col.DefaultValue!)};");
        // Nested-objects: a child table/object collapses into its parent record as a plain
        // settable member, typed the same as a top-level list/object member
        // (List<ns.Models.Child>? for a list child, ns.Models.Child? for an object child).
        // Initializer depends on the child's OWN role, not its parent's: an OUTPUT list
        // child gets no initializer (matches top-level Response lists, which are
        // null-when-absent), while an INPUT list child KEEPS the `= []` initializer
        // (matches top-level Request lists — Task 13's generator output). Object
        // children (either side) never get one.
        if (hasChildren)
            foreach (var child in children!)
            {
                string initializer = child.Role == VariableRole.Params ? " = [];" : "";
                lines.Add($"    public {GetPropertyType(child, modelsNs)} {child.Name} {{ get; set; }}{initializer}");
            }
        lines.Add("}");
        lines.Add("");
    }

    /// <summary>
    /// Approximates the generator's per-type default initializer for a column
    /// <c>DEFAULT &lt;raw&gt;</c>: decimal gets an <c>m</c> suffix, single-quoted SQL
    /// strings become double-quoted C#, everything else is emitted as-is (date/guid
    /// are approximate in the preview — the real generator wraps them in a Parse call).
    /// </summary>
    private static string CSharpDefault(string sqlType, string raw)
    {
        if (raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'')
            return $"\"{raw.Substring(1, raw.Length - 2)}\"";

        string @base = sqlType.ToLowerInvariant().Split('(')[0].Trim();
        return @base is "decimal" or "numeric" or "money" or "smallmoney" ? $"{raw}m" : raw;
    }

    private static void EmitModelRecord(
        List<string> lines, string typeName, List<SQuiLVariable> vars, bool isResponse,
        IReadOnlyList<SQuiLVariable>? allVars = null, string? modelsNs = null)
    {
        lines.Add($"public partial record {typeName}");
        lines.Add("{");

        // *Request specials are OPT-IN — each appears only when its bare special
        // is declared in the SQL header.  @Debug → bool Debug, @SuppressDebug →
        // bool SuppressDebug (replaces the old always-on DebugOnly), @AsOfDate →
        // a nullable typed property.  @EnvironmentName is a sent parameter only,
        // never a property.
        if (!isResponse)
        {
            var declared = allVars ?? new List<SQuiLVariable>();
            bool hasDebug = declared.Any(v => v.Role == VariableRole.Debug);
            bool hasSuppressDebug = declared.Any(v => v.Role == VariableRole.SuppressDebug);
            var asOfDate = declared.FirstOrDefault(v => v.Role == VariableRole.AsOfDate);

            if (hasDebug) lines.Add("    public bool Debug { get; set; }");
            if (hasSuppressDebug) lines.Add("    public bool SuppressDebug { get; set; }");
            if (asOfDate != null)
            {
                // Take only the type token (drop any "= default" the SQL initializer
                // adds), matching the generator which maps the bare declared type.
                // AsOfDate is always nullable on *Request.
                string asOfType = asOfDate.SqlType.Split(new[] { ' ', '=' }, 2)[0];
                lines.Add($"    public {SqlTypeMap.SqlToCSharp(asOfType)}? AsOfDate {{ get; set; }}");
            }

            if ((hasDebug || hasSuppressDebug || asOfDate != null) && vars.Count > 0) lines.Add("");
        }

        foreach (var v in vars)
        {
            string type = GetPropertyType(v, modelsNs);
            string initializer = (!isResponse && IsCollection(v)) ? " = []" : "";
            lines.Add($"    public {type} {v.Name} {{ get; set; }}{initializer};");
        }

        lines.Add("}");
        lines.Add("");
    }
}
