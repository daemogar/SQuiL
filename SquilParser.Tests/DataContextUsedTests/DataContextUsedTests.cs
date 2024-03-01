/*using Testcontainers.MsSql;

namespace SquilParser.Tests;

public class DataContextUsedTests : IAsyncLifetime
{
	private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
		.WithImage("mcr.microsoft.com/mssql/server:latest")
		.WithEnvironment("ACCEPT_EULA", "Y")
		.WithEnvironment("SA_PASSWORD", "Your_password123")
		//.WithCopyFileToContainer("init.sql", "/")
		.WithCommand("/opt/mssql-tools/bin/sqlcmd -S localhost -U SA -P \"Your_password123\" -i init.sql")
		.Build();

	public Task DisposeAsync() => _dbContainer.StartAsync();

	public Task InitializeAsync() => _dbContainer.StopAsync();
}
*/