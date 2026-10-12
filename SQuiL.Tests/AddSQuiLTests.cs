using Microsoft.Extensions.DependencyInjection;

using SQuiL.Tests.Sqlite;

namespace SQuiL.Tests;

/// <summary>
/// The generated <c>AddSQuiL()</c> is idempotent per service collection, with no process-wide
/// state, so independent hosts (and parallel tests) each get their own registrations.
/// </summary>
public class AddSQuiLTests
{
	[Fact]
	public void Each_service_collection_gets_its_own_registrations()
	{
		var first = new ServiceCollection().AddSQuiL();
		var second = new ServiceCollection().AddSQuiL();

		Assert.Single(first, p => p.ServiceType == typeof(SqliteNestedOutputDataContext));
		Assert.Single(second, p => p.ServiceType == typeof(SqliteNestedOutputDataContext));
	}

	[Fact]
	public void Calling_twice_on_one_collection_registers_once()
	{
		var services = new ServiceCollection();
		services.AddSQuiL();
		services.AddSQuiL();

		Assert.Single(services, p => p.ServiceType == typeof(SqliteNestedOutputDataContext));
	}

	[Fact]
	public void Registers_each_context_as_a_singleton()
	{
		var services = new ServiceCollection().AddSQuiL();

		var descriptor = Assert.Single(services, p => p.ServiceType == typeof(SqliteNestedOutputDataContext));
		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
	}
}
