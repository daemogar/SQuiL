namespace SQuiL;

using Microsoft.Extensions.Configuration;

using System;

/// <summary>
/// Base class for all SQuiL-generated data context classes.
/// Provides SQL connection management, parameter construction, and environment resolution.
/// Generated data contexts inherit from this class and call its members to execute queries.
/// </summary>
/// <param name="Configuration">The <see cref="IConfiguration"/> instance used to look up connection strings.</param>
public abstract partial class SQuiLBaseDataContext(IConfiguration Configuration)
{
	/// <summary>
	/// The current environment name (e.g. "Development", "Production").
	/// Resolved first from the "EnvironmentName" config key, then from the environment
	/// variable named by "EnvironmentVariable" config key, then defaults to "Development".
	/// </summary>
	protected string EnvironmentName { get; } = Configuration.GetSection("EnvironmentName")?.Value
		?? Environment.GetEnvironmentVariable(Configuration.GetSection("EnvironmentVariable")?.Value ?? "ASPNETCORE_ENVIRONMENT")
		?? "Development";
}
