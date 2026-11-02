namespace SQuiL.Tests.SqlServer;

using System;
using System.Threading.Tasks;

using Testcontainers.MsSql;

using Xunit;

/// <summary>
/// The live SQL Server harness for <see cref="SqlServerRoundTripTests"/> and
/// <see cref="SqlServerKeyParityTests"/>, the SQL Server twin of
/// <c>PostgresContainerFixture</c>. Starts a real SQL Server 2022 container through
/// Testcontainers and exposes its connection string.
///
/// <para>
/// <b>Existing server</b>: when <c>SQUIL_TEST_MSSQL</c> holds a connection string, the tests use
/// that server instead of starting a container, for machines with no Docker/Podman (for example
/// <c>Server=host,1433;User ID=sa;Password=...;Database=tempdb;TrustServerCertificate=True</c>).
/// It must name a database, since generated SQL Server code runs the query in the connection
/// string's database. The tests only use table variables, so any database works and nothing is
/// created on the server.
/// </para>
///
/// <para>
/// <b>Skippable when no server is available</b>: <see cref="InitializeAsync"/> catches a
/// container-start failure instead of failing the collection, and exposes it as
/// <see cref="StartupFailure"/> so each test can <c>Assert.Skip</c>.
/// </para>
/// </summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
	private static readonly string? ExternalConnectionString
		= Environment.GetEnvironmentVariable("SQUIL_TEST_MSSQL") is { Length: > 0 } value ? value : null;

	// Built in InitializeAsync, not here: building probes for Docker and throws in the constructor
	// when none is reachable, which would fail the collection instead of letting it skip.
	private MsSqlContainer? _container;

	/// <summary>
	/// Non-null when <see cref="InitializeAsync"/> could not start a container (no Docker/Podman
	/// runtime reachable, etc.). Tests should check this first and skip rather than fail.
	/// </summary>
	public Exception? StartupFailure { get; private set; }

	/// <summary>The live server's connection string. Only valid when <see cref="StartupFailure"/> is null.</summary>
	public string ConnectionString => ExternalConnectionString ?? _container!.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		if (ExternalConnectionString is not null)
			return;

		try
		{
			_container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
			await _container.StartAsync();
		}
		catch (Exception ex)
		{
			StartupFailure = ex;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_container is not null)
			await _container.DisposeAsync();
	}
}

/// <summary>
/// Shared xUnit collection for every test that needs the live SQL Server. The tests in it run one at
/// a time against one server, and the container starts once for the whole collection.
/// </summary>
[CollectionDefinition("SqlServer container")]
public sealed class SqlServerContainerCollection : ICollectionFixture<SqlServerContainerFixture>
{
}
