namespace SQuiL.Generator;

public record SQuiLDependency(string Type)
{
	public bool DependencyInjection { get; init; }
	public bool Configuration { get; init; }
	public bool DataSqlClient { get; init; }
};
