using SQuiL.Models;

using SQuiL.SourceGenerator.Parser;

namespace Microsoft.CodeAnalysis;

public class SQuiLTableMap
{
	private Dictionary<string, List<(string Attribute, Location Location)>> Mappings { get; } = [];

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

	private Dictionary<string, (SQuiLTable Table, List<CodeItem> Items)> Dictionary { get; } = [];

	public IEnumerable<string> TableNames => Dictionary.Keys.OrderBy(p => p);

	public void Add(string name, string map, Location location)
	{
		if (!Mappings.ContainsKey(name))
			Mappings.Add(name, []);
		Mappings[name].Add((map, location));
	}

	public void Add(SQuiLTable property)
	{
		if (!Dictionary.ContainsKey(property.OriginalName))
			Dictionary.Add(property.OriginalName, (property, []));

		foreach (var item in property.CodeItems)
			if (!Dictionary[property.OriginalName].Items.Any(p => p.Identifier.Value == item.Identifier.Value))
				Dictionary[property.OriginalName].Items.Add(item);
	}

	public bool TryGetValue(string value, out string tableName, out List<CodeItem> properties)
	{
		TryGetName(value, out tableName);
		properties = Dictionary.TryGetValue(value, out var table) ? table.Items : [];
		return properties.Count > 0;
	}

	public bool TryGetName(string value, out string tableName)
	{
		tableName = value;
		if (value is null || !Mappings.TryGetValue(value, out var map))
			return false;

		tableName = map.First().Attribute;
		return true;
	}

	public void GenerateCode(
		out List<(string Name, string Text)> sources,
		out List<Exception> exceptions)
	{
		sources = [];
		exceptions = [];

		foreach (var (table, items) in Dictionary.Values)
		{
			var (name, text) = table.GenerateCode(items);
			if (text.TryGetValue(out var value, out var exception))
			{
				sources.Add((name, value));
				continue;
			}

			if (exception is AggregateException e)
				exceptions.AddRange(e.InnerExceptions);
			else if (exception is not null)
				exceptions.Add(exception);
		}
	}
}
