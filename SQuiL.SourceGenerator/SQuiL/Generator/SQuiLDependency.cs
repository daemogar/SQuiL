namespace SQuiL.Generator;

/// <summary>
/// Represents a single metadata reference found in the consumer project's compilation.
/// Each instance flags which SQuiL-required assembly the reference satisfies.
/// </summary>
/// <param name="Type">The DLL file name that matched.</param>
internal record SQuiLDependency(string Type)
{
	/// <summary><c>true</c> when the reference satisfies the <c>Microsoft.Extensions.DependencyInjection</c> requirement.</summary>
	public bool DependencyInjection { get; init; }
	/// <summary><c>true</c> when the reference satisfies the <c>Microsoft.Extensions.Configuration</c> requirement.</summary>
	public bool Configuration { get; init; }
	/// <summary><c>true</c> when the reference satisfies the <c>Microsoft.Data.SqlClient</c> requirement.</summary>
	public bool DataSqlClient { get; init; }
};
