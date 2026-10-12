using System.Collections.Immutable;

namespace SQuiL.Generator;

/// <summary>
/// Every record declaration in the consumer compilation, looked up either loosely by simple
/// name (base-type resolution) or strictly as the user's partial of a generated type.
/// </summary>
/// <remarks>See the SQuiL.SourceGenerator <c>README.md</c>, "User partial records".</remarks>
public sealed class SQuiLRecordIndex(ImmutableArray<SQuiLPartialModel> all)
{
	/// <summary>Every collected record, in compilation order.</summary>
	public ImmutableArray<SQuiLPartialModel> All { get; } = all;

	/// <summary>First record with the simple name <paramref name="name"/>, in any namespace.</summary>
	public bool TryGetValue(string name, out SQuiLPartialModel record)
	{
		foreach (var item in All)
			if (item.Name == name)
			{
				record = item;
				return true;
			}

		record = default!;
		return false;
	}

	/// <summary>The user's partial of the generated type <paramref name="name"/> emitted into
	/// <paramref name="namespace"/>: a mergeable record with that name in that namespace.</summary>
	public bool TryGetPartial(string name, string @namespace, out SQuiLPartialModel record)
	{
		foreach (var item in All)
			if (item.IsMergeable && item.Name == name && item.Namespace == @namespace)
			{
				record = item;
				return true;
			}

		record = default!;
		return false;
	}
}
