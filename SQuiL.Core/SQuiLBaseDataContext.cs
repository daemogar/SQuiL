namespace SQuiL;

using Microsoft.Extensions.Configuration;

using System;

/// <summary>
/// Base class for all SQuiL-generated data context classes.
/// Provides SQL connection management, parameter construction, and environment resolution.
/// Generated data contexts inherit from this class and call its members to execute queries.
/// </summary>
/// <param name="configuration">The <see cref="IConfiguration"/> instance used to look up connection strings.</param>
public abstract partial class SQuiLBaseDataContext(IConfiguration configuration)
{
	/// <summary>
	/// The configuration used to resolve connection strings and environment settings.
	/// Exposed to provider subclasses (e.g. <c>SqlServerDataContext</c>) so they read it from
	/// the base rather than re-capturing the constructor parameter.
	/// </summary>
	protected IConfiguration Configuration { get; } = configuration;

	/// <summary>
	/// The current environment name (e.g. "Development", "Production").
	/// Resolved first from the "EnvironmentName" config key, then from the environment
	/// variable named by "EnvironmentVariable" config key, then defaults to "Development".
	/// </summary>
	protected string EnvironmentName { get; } = configuration.GetSection("EnvironmentName")?.Value
		?? Environment.GetEnvironmentVariable(configuration.GetSection("EnvironmentVariable")?.Value ?? "ASPNETCORE_ENVIRONMENT")
		?? "Development";
}
