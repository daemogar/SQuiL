namespace SQuiL.Generator;

/// <summary>Discriminates the role of a class decorated with a SQuiL attribute.</summary>
internal enum SQuiLDefinitionType
{
	/// <summary>The attribute was not recognized as a SQuiL attribute.</summary>
	Invalid = 0,
	/// <summary>The class carries <c>[SQuiLQueryAttribute]</c> and represents a data-context entry point.</summary>
	Query = 'Q',
	/// <summary>The class carries <c>[SQuiLTableAttribute]</c> and represents a shared table-type mapping.</summary>
	TableType = 'T'
}
