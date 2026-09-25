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

    private static string GetPropertyType(SQuiLVariable v, string? modelsNs = null, EditorDialect dialect = EditorDialect.SqlServer)
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
        string cs = SqlTypeMap.SqlToCSharp(v.SqlType, dialect);
        return v.Nullable ? $"{cs}?" : cs;
    }

    private static bool IsCollection(SQuiLVariable v) =>
        v.Role is VariableRole.Params or VariableRole.Returns;

    // ── Nested-objects key graph (preview-only mirror of SQuiLKeyGraph.cs) ──

    /// <summary>Parent → its direct children (declaration order) plus a lookup for "is this
    /// variable someone's child" — the child collapses into the parent record and drops off
    /// the Response top level. <c>Embeds</c> marks which children are R1 "embed" nestings — the
    /// nested variable OWNS the shared key (a many-to-one lookup embedded into its FK carrier),
    /// mirroring the generator's <c>SQuiLKeyEdge.IsEmbed</c>.</summary>
    private sealed class NestedGraph
    {
        public List<SQuiLVariable> Roots { get; } = new();
        public Dictionary<SQuiLVariable, List<SQuiLVariable>> ChildrenOf { get; } = new();
        private readonly HashSet<SQuiLVariable> _children = new();
        private readonly HashSet<SQuiLVariable> _embeds = new();
        public bool IsChild(SQuiLVariable v) => _children.Contains(v);
        public void MarkChild(SQuiLVariable v) => _children.Add(v);
        public bool IsEmbed(SQuiLVariable v) => _embeds.Contains(v);
        public void MarkEmbed(SQuiLVariable v) => _embeds.Add(v);
        /// <summary>Container → key columns its embeds supply (elided from its record, R4).</summary>
        public Dictionary<SQuiLVariable, List<string>> ElidedKeysOf { get; } = new();
    }

    /// <summary>One container→nested link, local to the preview builder — mirrors the generator's
    /// <c>SQuiLKeyEdge</c> (<c>SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs</c>) closely
    /// enough to run the same R3 resolution below, without pulling in the full diagnostics-carrying
    /// <c>KeyGraph</c>/<c>KeyGraphEdge</c> types from <c>SQuiLLinter.cs</c> (this stays a preview,
    /// not a diagnostics source).</summary>
    private sealed class PreviewEdge
    {
        public SQuiLVariable Parent { get; set; } = null!;
        public SQuiLVariable Child { get; set; } = null!;
        public string KeyName { get; set; } = "";
        public bool IsEmbed { get; set; }
    }

    /// <summary>
    /// Minimal preview mirror of the generator's <c>SQuiLKeyGraph</c>
    /// (<c>SQuiL.SourceGenerator/SQuiL/Models/SQuiLKeyGraph.cs</c>): two table/object variables
    /// that share a key column name are linked; ORIENTATION follows declaration order — the
    /// earlier-declared variable is always the container (parent), regardless of which side owns
    /// the Primary Key (<c>NestedGraph.IsEmbed</c> records which). Variables nobody links to are
    /// roots. Called once for OUTPUT (<c>@Return*</c>) table/object variables and once for INPUT
    /// (<c>@Param*</c>) table/object variables (never mixed), matching the generator building one
    /// graph per side (FileGenerator.cs's <c>keyGraph</c> / <c>inputGraph</c>).
    ///
    /// UPDATE (Task 3): R3 multi-container resolution (shared lookups / many-to-many junctions) IS
    /// now ported here, unlike SP0033/SP0034/SP0035/SP0036 — those stay diagnostics-only, reported
    /// by the generator/linter, never by the preview. The distinction: R3 changes the SHAPE the
    /// preview renders (which variable nests under which), so skipping it made the preview actively
    /// WRONG for a common, valid pattern (a shared lookup silently vanished from every container but
    /// the first) — not just approximate. A genuinely ambiguous/cyclic file (SP0033/SP0034) is still
    /// a build error the generator/linter will squiggle; this preview does not re-detect cycles —
    /// R3 here can, in that pathological case, leave two variables each nested inside the other,
    /// which renders as slightly odd (mutually-referencing) preview text rather than crashing, since
    /// <c>EmitTableRecord</c> below is a flat, non-recursive pass over <c>tableVars</c>.
    /// </summary>
    private static NestedGraph BuildNestedGraph(List<SQuiLVariable> tableVars)
    {
        var pkOwner = new Dictionary<string, SQuiLVariable>(System.StringComparer.OrdinalIgnoreCase);
        var pkNameOf = new Dictionary<SQuiLVariable, string>();
        foreach (var v in tableVars)
        {
            var pk = v.Columns?.FirstOrDefault(c => c.IsPrimaryKey);
            if (pk is not null && !pkOwner.ContainsKey(pk.Name))
            {
                pkOwner[pk.Name] = v;
                pkNameOf[v] = pk.Name;
            }
        }

        // R1: orientation follows declaration order, not which side owns the Primary Key.
        // `tableVars` is already in declaration order, so its index is the declaration ordinal.
        var order = new Dictionary<SQuiLVariable, int>();
        for (var i = 0; i < tableVars.Count; i++) order[tableVars[i]] = i;

        // Distinct unordered pairs {block, pkOwner} that share a key column name. Dedupe is keyed
        // on the PAIR alone (lo, hi) — matching the generator/linter/VS Code copies — so two
        // reciprocal key columns between the same two blocks still yield exactly one edge, and R3
        // below only ever sees a genuine THIRD block as a competing container.
        var pairs = new List<(SQuiLVariable A, SQuiLVariable B, string Key)>();
        var pairSeen = new HashSet<(int, int)>();
        foreach (var block in tableVars)
        {
            foreach (var col in block.Columns ?? new List<TableColumn>())
            {
                if (!pkOwner.TryGetValue(col.Name, out var owner) || ReferenceEquals(owner, block))
                    continue;
                var lo = System.Math.Min(order[block], order[owner]);
                var hi = System.Math.Max(order[block], order[owner]);
                if (!pairSeen.Add((lo, hi))) continue;
                pairs.Add((tableVars[lo], tableVars[hi], col.Name));
            }
        }

        // R1: the earlier-declared variable is the container (parent). Embed when the
        // later-declared (nested) variable owns the shared key as its own Primary Key.
        var edges = new List<PreviewEdge>();
        foreach (var (a, b, key) in pairs)
        {
            var nestedOwnsKey = pkNameOf.TryGetValue(b, out var bKey)
                && string.Equals(bKey, key, System.StringComparison.OrdinalIgnoreCase);
            edges.Add(new PreviewEdge { Parent = a, Child = b, KeyName = key, IsEmbed = nestedOwnsKey });
        }

        // R3 (Task 3): a variable with more than one container is either a shared lookup (it owns
        // the key in EVERY such edge — allowed, each container references the same row) or a
        // junction / mixed case (keep the earliest-declared container; invert the rest so the
        // dropped container becomes an embed INTO this variable). Dropping or inverting can create
        // a NEW multi-container variable, so iterate until stable. Kept in the preview (not just
        // build/lint) because silently dropping a legitimate shared-lookup child (the pre-R3
        // "first link wins" behavior) rendered a WRONG shape, not just an approximate one — see
        // the class doc comment above.
        //
        // TERMINATION PROOF (review round 1, C2 — `guard < tableVars.Count + 1` was NOT a valid
        // bound; see SQuiLKeyGraph.cs's identical comment for the full proof): the pair
        // `(#nonEmbed, #edges)`, ordered lexicographically, strictly decreases every iteration —
        // every qualifying group has at least one non-embed edge (the `!g.All(...)` guard
        // excludes all-embed groups), and dropping a non-embed edge always inverts it (`#nonEmbed`
        // falls), while dropping an already-embed edge never re-inverts (`#edges` falls, since key
        // ownership is unique per name). Both counters are bounded below by 0 and start at most
        // `edges.Count`, so the loop terminates within `2 * edges.Count` iterations. Hitting that
        // bound is proof of a bug in this algorithm, not a possible user file.
        var guardLimit = 2 * edges.Count;
        for (var guard = 0; ; guard++)
        {
            var byNested = edges.GroupBy(e => e.Child)
                .FirstOrDefault(g => g.Count() > 1 && !g.All(e => e.IsEmbed));
            if (byNested is null) break;
            if (guard >= guardLimit)
                throw new System.InvalidOperationException(
                    $"BuildNestedGraph R3 resolution did not reach a fixed point within {guardLimit} " +
                    "iterations. This violates the algorithm's proven termination bound and indicates " +
                    "a bug in BuildNestedGraph's R3 loop, not a malformed query file.");

            var ordered = byNested.OrderBy(e => order[e.Parent]).ToList();
            foreach (var drop in ordered.Skip(1))
            {
                edges.Remove(drop);
                if (pkNameOf.TryGetValue(drop.Parent, out var parentKey)
                    && string.Equals(parentKey, drop.KeyName, System.StringComparison.OrdinalIgnoreCase))
                    edges.Add(new PreviewEdge { Parent = drop.Child, Child = drop.Parent, KeyName = drop.KeyName, IsEmbed = true });
            }
        }

        var graph = new NestedGraph();
        var hasParent = new HashSet<SQuiLVariable>();
        foreach (var e in edges)
        {
            hasParent.Add(e.Child);
            graph.MarkChild(e.Child);
            if (e.IsEmbed)
            {
                graph.MarkEmbed(e.Child);
                if (!graph.ElidedKeysOf.TryGetValue(e.Parent, out var keys))
                    graph.ElidedKeysOf[e.Parent] = keys = new List<string>();
                keys.Add(e.KeyName);
            }
            if (!graph.ChildrenOf.TryGetValue(e.Parent, out var list))
                graph.ChildrenOf[e.Parent] = list = new List<SQuiLVariable>();
            list.Add(e.Child);
        }
        graph.Roots.AddRange(tableVars.Where(v => !hasParent.Contains(v)));
        return graph;
    }

    public static string Generate(SQuiLParseResult parsed, string queryName, string ns = "YourNamespace", bool enabled = false, bool debugRollback = true, EditorDialect dialect = EditorDialect.SqlServer)
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
        bool IsEmbed(SQuiLVariable v) => outputGraph.IsEmbed(v) || inputGraph.IsEmbed(v);
        List<string>? ElidedKeysOf(SQuiLVariable v) =>
            outputGraph.ElidedKeysOf.TryGetValue(v, out var ok) ? ok :
            inputGraph.ElidedKeysOf.TryGetValue(v, out var ik) ? ik : null;

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
        EmitModelRecord(lines, $"{queryName}Request", requestVars, isResponse: false, parsed.Variables, modelsNs, dialect);

        // ── Response record (only nesting ROOTS appear at the top level — a
        // child collapses into its parent record as a member instead) ──────
        if (returnVars.Count > 0)
        {
            lines.Add("// ── Response ────────────────────────────────────────────");
            EmitModelRecord(lines, $"{queryName}Response", responseVars, isResponse: true, modelsNs: modelsNs, dialect: dialect);
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
                EmitTableRecord(lines, RecordTypeName(v), v, modelsNs, ChildrenOf(v), dialect, IsEmbed, ElidedKeysOf(v));
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
        string? modelsNs = null, List<SQuiLVariable>? children = null, EditorDialect dialect = EditorDialect.SqlServer,
        System.Func<SQuiLVariable, bool>? isEmbed = null, List<string>? elidedKeys = null)
    {
        if (v.Columns is null || v.Columns.Count == 0) return;

        // R4: a column an embed supplies is dropped from the record.
        bool IsElided(TableColumn c) =>
            elidedKeys is not null && elidedKeys.Any(k => string.Equals(k, c.Name, System.StringComparison.OrdinalIgnoreCase));

        string CsType(TableColumn col)
        {
            string cs = SqlTypeMap.SqlToCSharp(col.SqlType, dialect);
            bool nullable = col.Nullable;
            return nullable ? cs + "?" : cs;
        }

        var positional = v.Columns.Where(c => c.DefaultValue is null && !IsElided(c)).ToList();
        var defaulted = v.Columns.Where(c => c.DefaultValue is not null && !IsElided(c)).ToList();
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
                // An embed is always a single object (R2), whatever its prefix.
                if (isEmbed?.Invoke(child) == true)
                {
                    string embedType = modelsNs is not null ? $"{modelsNs}.{RecordTypeName(child)}" : RecordTypeName(child);
                    lines.Add($"    public {embedType}? {child.Name} {{ get; set; }}");
                    continue;
                }
                string initializer = child.Role == VariableRole.Params ? " = [];" : "";
                lines.Add($"    public {GetPropertyType(child, modelsNs, dialect)} {child.Name} {{ get; set; }}{initializer}");
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
        IReadOnlyList<SQuiLVariable>? allVars = null, string? modelsNs = null, EditorDialect dialect = EditorDialect.SqlServer)
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
                lines.Add($"    public {SqlTypeMap.SqlToCSharp(asOfType, dialect)}? AsOfDate {{ get; set; }}");
            }

            if ((hasDebug || hasSuppressDebug || asOfDate != null) && vars.Count > 0) lines.Add("");
        }

        foreach (var v in vars)
        {
            string type = GetPropertyType(v, modelsNs, dialect);
            string initializer = (!isResponse && IsCollection(v)) ? " = []" : "";
            lines.Add($"    public {type} {v.Name} {{ get; set; }}{initializer};");
        }

        lines.Add("}");
        lines.Add("");
    }
}
