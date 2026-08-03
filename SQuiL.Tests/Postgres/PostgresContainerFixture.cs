namespace SQuiL.Tests.Postgres;

using System;
using System.Threading.Tasks;

using Testcontainers.PostgreSql;

using Xunit;

/// <summary>
/// Task 9 (Phase 3 Postgres): the LIVE-container harness for the PostgreSQL round-trip + live-reader
/// KeyParity facts (<see cref="PostgresRoundTripTests"/>, <see cref="PostgresKeyParityTests"/>).
/// Starts a real <c>postgres:17</c> container via Testcontainers and exposes its connection string.
///
/// <para>
/// <b>Container runtime</b> — Testcontainers auto-detects the local Docker/Podman endpoint; nothing
/// in this fixture is Docker- or Podman-specific. Prefer Podman where available (start it with
/// <c>podman machine start</c> and either point Testcontainers at the Podman API socket via
/// <c>DOCKER_HOST=npipe:////./pipe/podman-machine-default</c> on Windows, or set
/// <c>TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE</c>); Docker Desktop is the zero-config fallback and is
/// what this repo's dev machine runs. The container runtime is a TEST-HARNESS detail only — it is
/// not a product dependency (<c>SQuiL.Postgres</c> never references Testcontainers), and whether CI
/// runs these tests (which requires a container runtime on the build agent) is Paul's call.
/// </para>
///
/// <para>
/// <b>Skippable when no runtime is present</b> — <see cref="InitializeAsync"/> catches any
/// container-start failure (missing Docker/Podman, no permission to the socket, etc.) instead of
/// letting it fail the whole collection; the caught exception is exposed via
/// <see cref="StartupFailure"/> so every fact in the collection can <c>Assert.Skip(...)</c> up front
/// (see the <c>SkipIfUnavailable()</c> helper on each test class) rather than reporting a hard
/// failure on a machine with no container runtime.
/// </para>
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
		.Build();

	/// <summary>
	/// Non-null when <see cref="InitializeAsync"/> could not start a container (no Docker/Podman
	/// runtime reachable, etc.). Tests should check this first and skip rather than fail.
	/// </summary>
	public Exception? StartupFailure { get; private set; }

	/// <summary>The live container's connection string. Only valid when <see cref="StartupFailure"/> is null.</summary>
	public string ConnectionString => _container.GetConnectionString();

	public async ValueTask InitializeAsync()
	{
		try
		{
			await _container.StartAsync();
		}
		catch (Exception ex)
		{
			// No Docker/Podman runtime reachable on this machine (or the image pull failed, etc.) —
			// record the failure instead of throwing, so every dependent fact can skip cleanly.
			StartupFailure = ex;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (StartupFailure is null)
			await _container.DisposeAsync();
	}
}

/// <summary>
/// Shared xUnit collection for every fact that needs the live PostgreSQL container. Membership in
/// one collection guarantees xUnit runs these facts sequentially against the one container instance
/// (never in parallel with each other), and the container starts once for the whole collection.
/// </summary>
[CollectionDefinition("Postgres container")]
public sealed class PostgresContainerCollection : ICollectionFixture<PostgresContainerFixture>
{
}
