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
	/// Registers a <see cref="SQuiLTable"/> and merges its columns into the shared dictionary.
	/// If the same table was already added by another query, any new columns are appended.
	/// </summary>
	/// <param name="property">The table model to register.</param>
	public void Add(SQuiLTable property)
	{
		if (!Dictionary.ContainsKey(property.OriginalName))
			Dictionary.Add(property.OriginalName, (property, []));

		foreach (var item in property.CodeItems)
			if (!Dictionary[property.OriginalName].Items.Any(p => p.Identifier.Value == item.Identifier.Value))
				Dictionary[property.OriginalName].Items.Add(item);
	}

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
