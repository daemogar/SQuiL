using Testcontainers.MsSql;

namespace SquilParser.Tests;

public class DataContextUsedTests : IAsyncLifetime
{
	private readonly MsSqlContainer _dbContainer
		= new MsSqlBuilder().Build();

	public Task DisposeAsync() => _dbContainer.StartAsync();

	public Task InitializeAsync() => _dbContainer.StopAsync();
}
