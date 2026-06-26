using SQuiL.Models;

using SQuiL.SourceGenerator.Parser;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Tracks the mapping between SQL table-variable names and the C# record types that
/// represent them, and detects duplicate mappings. Also owns code generation for all
/// shared table types referenced across one or more queries in the project.
/// </summary>
public class SQuiLTableMap
{
	/// <summary>
	/// Raw mapping registrations: table name → list of (C# class name, source location).
	/// Used to detect duplicate <c>[SQuiLTableAttribute]</c> mappings for the same table name.
	/// </summary>
	private Dictionary<string, List<(string Attribute, Location Location)>> Mappings { get; } = [];

	/// <summary>
	/// Returns any table names that have been registered more than once, indicating a conflict.
	/// </summary>
	/// <param name="issues">Output dictionary of conflicting table names and their registrations.</param>
	/// <returns><c>true</c> if at least one duplicate exists.</returns>
	public bool TryGetMappingIssues(out Dictionary<string, List<(string Attribute, Location Location)>> issues)
	{
		issues = [];

		foreach (var maps in Mappings)
		{
			if (maps.Value.Count > 1)
				issues.Add(maps.Key, maps.Value);
		}

		return issues.Count > 0;
	}

	/// <summary>
	/// Resolved table entries: original SQL name → (<see cref="SQuiLTable"/> model, merged column list).
	/// Multiple queries can reference the same table type; columns are union-merged here.
	/// </summary>
	private Dictionary<string, (SQuiLTable Table, List<CodeItem> Items)> Dictionary { get; } = [];

	/// <summary>All known SQL table-variable names in alphabetical order.</summary>
	public IEnumerable<string> TableNames => Dictionary.Keys.OrderBy(p => p);

	/// <summary>
	/// Registers a name-to-class mapping from a <c>[SQuiLTableAttribute]</c> or
	/// <c>[SQuiLQueryAttribute]</c> decoration so the generator can resolve the C# type name
	/// for a given SQL table variable.
	/// </summary>
	/// <param name="name">The SQL table-variable base name (e.g. <c>Students</c>).</param>
	/// <param name="map">The C# class name the table maps to.</param>
	/// <param name="location">Source location of the attribute, used for duplicate diagnostics.</param>
	public void Add(string name, string map, Location location)
	{
		if (!Mappings.ContainsKey(name))
			Mappings.Add(name, []);
		Mappings[name].Add((map, location));
	}

	/// <summary>
	/// SP0017 conflicts: declarations that share one generated record type but declare
	/// different column shapes (name, type, nullability, and order must all match).
	/// <c>FirstSourceName</c> is the query method/file name of the first registrant, or
	/// empty string when the conflict was detected during the cross-query merge phase.
	/// </summary>
	private List<(string TableName, string Expected, string Actual, string FirstSourceName)> ShapeConflicts { get; } = [];

	/// <summary>
	/// Returns any shape conflicts collected while registering and merging tables.
	/// Populated by <see cref="Add(SQuiLTable)"/> and <see cref="GenerateCode"/>, so only
	/// complete after code generation has run.
	/// </summary>
	/// <param name="issues">Output list of (record name, expected shape, conflicting shape, first source name).</param>
	/// <returns><c>true</c> if at least one conflict exists.</returns>
	public bool TryGetShapeIssues(out List<(string TableName, string Expected, string Actual, string FirstSourceName)> issues)
	{
		issues = ShapeConflicts;
		return issues.Count > 0;
	}

	/// <summary>
	/// SP0021 conflicts: declarations that share one generated record type but resolve to
	/// different record namespaces via their <c>[SQuiLQuery(..., Namespace: ...)]</c> arguments.
	/// </summary>
	private List<(string TableName, string First, string Second)> NamespaceConflicts { get; } = [];

	/// <summary>
	/// Returns any namespace conflicts collected while registering tables.
	/// </summary>
	/// <param name="issues">Output list of (record name, first namespace, conflicting namespace).</param>
	/// <returns><c>true</c> if at least one conflict exists.</returns>
	public bool TryGetNamespaceIssues(out List<(string TableName, string First, string Second)> issues)
	{
		issues = NamespaceConflicts;
		return issues.Count > 0;
	}

	/// <summary>
	/// Registers a <see cref="SQuiLTable"/> and merges its columns into the shared dictionary.
	/// If the same table was already added by another query, any new columns are appended.
	/// Declarations that share a name but differ in shape are recorded as SP0017 conflicts —
	/// the merged record's positional constructor cannot serve mismatched shapes.
	/// </summary>
	/// <param name="property">The table model to register.</param>
	public void Add(SQuiLTable property)
	{
		if (Dictionary.TryGetValue(property.OriginalName, out var existing))
		{
			if (!SameShape(existing.Items, property.CodeItems))
				ShapeConflicts.Add((property.TableName(), Shape(existing.Items), Shape(property.CodeItems), existing.Table.SourceName));
			if (existing.Table.RecordNamespace != property.RecordNamespace)
				NamespaceConflicts.Add((property.TableName(), existing.Table.RecordNamespace, property.RecordNamespace));
		}
		else
			Dictionary.Add(property.OriginalName, (property, []));

		foreach (var item in property.CodeItems)
			if (!Dictionary[property.OriginalName].Items.Any(p => p.Identifier.Value == item.Identifier.Value))
				Dictionary[property.OriginalName].Items.Add(item);
	}

	/// <summary>
	/// Two column lists have the same shape when they match pairwise in order:
	/// same column name, same SQL type token, same nullability. Sizes may differ —
	/// each query emits its own SQL declaration, so only the shared C# record matters.
	/// </summary>
	private static bool SameShape(List<CodeItem> left, List<CodeItem> right)
		=> left.Count == right.Count
		&& left.Zip(right, (p, q)
			=> p.Identifier.Value == q.Identifier.Value
			&& p.Type.Type == q.Type.Type
			&& p.IsNullable == q.IsNullable).All(p => p);

	/// <summary>Renders a column list as a readable shape string for SP0017 messages.</summary>
	private static string Shape(IEnumerable<CodeItem> items)
		=> $"({string.Join(", ", items.Select(p => $"{p.Identifier.Value} {p.Type.Original ?? p.Type.Type.ToString()}{(p.IsNullable ? " Null" : "")}"))})";

	/// <summary>
	/// Looks up a table's merged column list and resolves its C# class name.
	/// </summary>
	/// <param name="value">The SQL table-variable base name.</param>
	/// <param name="tableName">
	/// Outputs the resolved C# type name (from <see cref="Mappings"/> if present,
	/// otherwise the original <paramref name="value"/>).
	/// </param>
	/// <param name="properties">Outputs the merged list of columns for the table.</param>
	/// <returns><c>true</c> if the table has at least one registered column.</returns>
	public bool TryGetValue(string value, out string tableName, out List<CodeItem> properties)
	{
		TryGetName(value, out tableName);
		properties = Dictionary.TryGetValue(value, out var table) ? table.Items : [];
		return properties.Count > 0;
	}

	/// <summary>
	/// Resolves a SQL table-variable name to its mapped C# class name.
	/// </summary>
	/// <param name="value">The SQL table-variable base name.</param>
	/// <param name="tableName">
	/// Outputs the first registered C# class name, or <paramref name="value"/> unchanged if no mapping exists.
	/// </param>
	/// <returns><c>true</c> if a mapping was found.</returns>
	public bool TryGetName(string value, out string tableName)
	{
		tableName = value;
		if (value is null || !Mappings.TryGetValue(value, out var map))
			return false;

		tableName = map.First().Attribute;
		return true;
	}

	/// <summary>
	/// Generates C# source for all shared table-type records, merging columns from every
	/// query that references each table so the emitted type covers the full column set.
	/// </summary>
	/// <param name="sources">Output map of type name → generated source text.</param>
	/// <param name="exceptions">Output list of any generation errors encountered.</param>
	public void GenerateCode(
		out Dictionary<string, string> sources,
		out List<Exception> exceptions)
	{
		sources = [];
		exceptions = [];

		foreach (var merge in Dictionary.Values.GroupBy(p => p.Table.TableName()))
		{
			var reference = merge.First().Items;
			foreach (var entry in merge.Skip(1))
				if (!SameShape(reference, entry.Items))
					ShapeConflicts.Add((merge.Key, Shape(reference), Shape(entry.Items), ""));

			// SP0017: a merged record with mismatched shapes cannot be emitted —
			// its positional constructor would break every reader that shares it.
			if (ShapeConflicts.Any(p => p.TableName == merge.Key))
				continue;

			List<string> names = [];
			List<CodeItem> items = [];

			foreach (var item in merge.SelectMany(q => q.Items))
			{
				var unique = item.UniqueIdentifier();
				if (names.Contains(unique))
					continue;

				names.Add(unique);
				items.Add(item);
			}

			var (name, text) = merge.First().Table.GenerateCode(items);

			if (text.TryGetValue(out var value, out var exception))
			{
				if (!string.IsNullOrEmpty(value))
					sources.Add(name, value);
				continue;
			}

			if (exception is AggregateException e)
				exceptions.AddRange(e.InnerExceptions);
			else if (exception is not null)
				exceptions.Add(exception);
		}
	}
}
