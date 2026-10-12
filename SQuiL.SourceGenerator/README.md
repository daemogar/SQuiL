# SQuiL.SourceGenerator

The Roslyn incremental generator that turns `.squil` / `.sql` query files into typed C#. It is not
packed on its own: `SQuiL.Core` packs this project's DLL into `analyzers/dotnet/cs`. The
consumer-facing docs live in the repository `README.md` and `CLAUDE.md`. This file records
implementation rationale that is too long for code comments.

## User partial records

`SQuiLGenerator` collects every `record` declaration in the consumer compilation into a
`SQuiLRecordIndex`. The index answers two different questions:

- **Is this the user's partial of a generated type?** (`TryGetPartial`) Only a record that is
  `partial`, top-level (not nested in another type), and declared in the namespace the generated
  type lands in counts. Row records land in `RecordNamespace` (`<Ctx>.Models` by default), while
  `[SQuiLTable]`-mapped records and the `*Request`/`*Response` models land in the context
  namespace. A merge changes what is emitted. A user primary constructor makes the generator emit
  nothing for that type, and an `internal` modifier fails generation. So a false match is a real bug, not a
  cosmetic one.
- **What does this base record declare?** (`TryGetValue`) A base type named in a partial's base
  list need not be partial or share its namespace. It still resolves by simple name, first match
  wins.

Before TODO #27 both questions used one dictionary keyed by simple name. Any unrelated record
with the same name, such as a non-partial `Colleague.Institution`, then suppressed
`<Ctx>.Models.Institution`, and the consumer got `CS0234` with no SQuiL diagnostic. See
`SQuiL.Tests/SameNameRecordTests/`.

## Nested objects: key graph

`SQuiL/Models/SQuiLKeyGraph.cs` builds one graph per query file side (OUTPUT `@Return*` blocks and
INPUT `@Param*` blocks, never mixed). The editors carry mirrors that must stay in step:
`keyGraph.ts` (VS Code) and `SQuiLLinter.BuildKeyGraph` (SSMS and Visual Studio, byte-identical
modulo namespace). The previews derive from those mirrors.

### Rules

- **R0: one owner per key name.** Only one block may declare `Primary Key` on a given column name.
  A second claimant is SP0033. Its marker is then ignored everywhere: orientation, `IsEmbed`, and
  orphan hints.
- **R1: declaration order decides containment.** Two blocks that share a key column name are linked.
  The earlier-declared block is always the container (`Parent`). `IsEmbed` is true when the nested
  block (`Child`) owns the key, which makes it a lookup embedded into its FK carrier. It is false
  when the nested block merely carries the key, which is the classic one-to-many child. Pairs are
  deduped on the block pair alone, so two reciprocal key columns between the same two blocks still
  produce one edge. Without that dedup, two properties would be emitted for one child (CS0102).
- **R2: an embed is always a single object,** whatever the nested block's own prefix says.
- **R3: multi-container resolution.** A block with 2+ containers is handled by its edges:
  - If it owns the key in every one of them, it is a **shared lookup**. That is allowed, and each
    container references the same row.
  - Otherwise it is a **junction** or a mixed case. The earliest-declared container is kept. Every
    other edge is dropped, and it is inverted into an embed when the dropped container owns the
    key, so the dropped container becomes a lookup of this block.
  - Dropping or inverting can create new multi-container blocks, so the loop runs to a fixed point.
- **R4: the container's FK column is elided** from the C# record for every embed edge.
  - **Output:** the reader keeps the value in an index-aligned parallel list
    `__<Block>__<Key>`, and the stitch matches on it.
  - **Input:** the flatten copies the key up from the embedded object into the same kind of list,
    and never synthesizes it. See "Input flatten" below.

`KeyName` is always the carrier's spelling of the column. Matching is case-insensitive, so the
owner's record may spell it differently. Emitted code therefore uses the owner's own column
spelling on the owner side.

### R3 terminates

Every edge keeps the invariant `IsEmbed == Child owns KeyName`, including inverted ones: an
inversion only happens when the dropped edge's `Parent` owns the key, and the new edge's `Child`
is that `Parent`. Key ownership is unique per name (R0), so the rest follows:

- **Dropping an embed edge** never re-inverts it. `#edges` falls.
- **Dropping a non-embed edge** always inverts it, because its `Parent` owns the key by
  construction. `#nonEmbed` falls and `#edges` is unchanged.
- **Every qualifying group** contains at least one non-embed edge, because all-embed groups are
  excluded.

So the pair `(#nonEmbed, #edges)` strictly decreases, in lexicographic order, on every iteration.
Both counters start at no more than the edge count, so the loop ends within `2 * edges.Count`
iterations. The code throws if it ever hits that bound. Hitting it would mean a bug in the
algorithm, not a bad query file.

An earlier bound of `blocks + 1` was wrong: one inversion can spawn several new conflicts.

### Cycles (SP0034)

Every raw edge points from an earlier block to a later one, so edge construction alone cannot
produce a cycle. R3's inversion can point an edge backward, though, and a cascade of inversions
can close a loop. The minimal case needs 4 blocks; 3 was checked exhaustively. Here is a
realistic 4-block example:

```sql
Declare @Returns_Summary table(ProductID varchar(10));
Declare @Returns_Category table(CategoryID int Primary Key, Name varchar(50));
Declare @Returns_Product table(ProductID varchar(10) Primary Key, CategoryID int, Title varchar(50));
Declare @Returns_Junction table(CategoryID int, ProductID varchar(10), Note varchar(50));
```

So SP0034 is reachable from a file that looks valid. The message says so and suggests reordering
the declarations. Making R3 cycle-aware (dropping an edge instead of inverting it when the
inversion would close a loop) would change R3's semantics, and that decision has not been made.

The cycle check is a white/gray/black DFS over **every** edge. A last-write-wins `childOf` map
(one parent per child) is not enough. A shared lookup legitimately keeps several parents, so such
a map can discard exactly the edge that closes a cycle. A missed cycle is worse than a wrong
diagnostic: `SQuiLDataContext.DeepestFirstEdges` recurses over the same edges. It keeps a
`visited` set as a second line of defence, and that set also stops a shared lookup's descendants
from being stitched once per container.

### Other diagnostics

- **SP0035 (orphan PK):** a PK owner is an orphan when its key name is on no surviving edge. It is
  not enough that the owner is never a `Parent`, because embed owners are always the `Child`.
  SP0035 is only reported when the graph has at least one link.
- **SP0036 (unsynthesizable key):** checked on the classic (child) direction of the INPUT graph
  only. It is skipped for embed edges and for classic children of an embedded lookup, because in
  both cases the key comes from the caller.
- **SP0045 (containment hint):** an editor-only hint. It is emitted by `nestedObjectHints.ts` and
  `SQuiLLinter.LintContainmentHint`, not by the generator.

### Input flatten

`SQuiLDataContext.EmitInputFlatten` walks the nested request and builds one flat `__<Block>`
list per table:

- **Classic keys** are synthesized: integer keys are 1-based sequential values per table, and
  `uniqueidentifier` keys get `Guid.NewGuid()`. Each synthesized key is copied down into the
  child's FK column.
- **Embedded lookups** keep their caller-supplied key. That key is copied up into the
  container's parallel list and passed down to the lookup's own classic children.
- **Embedded rows are deduped by key.** A repeated key must match on every declared column and on
  the keys its own embeds would copy up. Otherwise it throws `Exception` ("Conflicting values").
- **A repeated, different instance that carries classic children** (a non-empty list child, or a
  non-null object child) throws `InvalidOperationException`. Children are only walked on the
  first sighting, so the second instance's children would otherwise be lost silently. Reusing
  one instance is fine.
- **A missing embed behind a `not null` container column** throws `InvalidOperationException`
  instead of sending NULL.

## Shape key

Each result set a query returns is routed to its declared `@Return*` output by an ordered
signature: `name:type` per column, joined by `|`, names lower-cased, a trailing `?` stripped. The
build side is `SQuiL/Models/SQuiLShapeKey.cs`, which emits `case "personid:int|name:string":`
labels. The runtime side is `SQuiLBaseDataContext.ShapeKey`, which reads `reader.GetName` and
`reader.GetDataTypeName` and passes the type through each provider's `NormalizeType`. The two
must agree, and `SQuiL.Tests/ShapeDetection/KeyParityTests.cs` and
`SQuiL.Tests/Postgres/PostgresKeyParityTests.cs` guard that. SQLite uses coarser tokens
(`bool`, `DateTime` and `Guid` become `long` or `string`), because a live SQLite reader only
reports a storage-class affinity.

### What the key leaves out

The type token is the mapped C# type, so **length, precision, and `varchar` vs `nvarchar` are not
part of the key**. `varchar(50)` and `nvarchar(100)` are both `string`; `decimal(18,2)` and
`decimal(10,4)` are both `decimal`. `int` vs `bigint` and `date` vs `datetime` still differ,
because their C# types differ. Two outputs in one file that differ only by length, precision, or
`varchar`/`nvarchar` therefore collide, which is SP0030
(`SQuiL.Tests/ShapeDetection/ShapeCollisionTests.cs`, `LengthOnlyDifferenceStillCollidesSP0030`).

Why length stays out:

- **It never affects a read.** A `varchar(50)` and a `varchar(100)` column read into the same
  `string`.
- **The runtime length comes from the `Select`, not the `Declare`.** In direct-select style the
  columns carry their source table's lengths. A key with length in it would silently skip a
  result set whose source column is `varchar(80)` when the output declares `varchar(100)`. The
  published guide promises that "length casts are never required" (`SKILL.md`, direct-select
  style).
- **The cost is real.** `GetDataTypeName` carries no length on SQL Server, so reading it needs
  `GetColumnSchema` (`ColumnSize`, `NumericPrecision`, `NumericScale`) plus handling for `max`
  (-1). PostgreSQL's `NormalizeType` has to strip the facets Npgsql does report, and SQLite
  routes by storage-class affinity, so length can't take part there.

### Parked: tie-break escalation

The considered alternative was to keep this key as the default and add the full facets (length,
precision, `nvarchar`) **only** to outputs that already collide on it. That would confine the
`GetColumnSchema` cost and the silent-skip risk to those files. It was rejected on 2026-10-12
(TODO #16) because no real query had hit a length-only collision. SP0030 already gives a clear
remedy: rename or reorder a column, change a C# type, or merge the two outputs under one name.
Revisit only if a real consumer hits a length-only SP0030. Even then, SQLite would keep
reporting SP0030, because affinity-based routing has no length to break the tie.
