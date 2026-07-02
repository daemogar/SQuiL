namespace SQuiL;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

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

	/// <summary>
	/// Builds a <see cref="SqlConnectionStringBuilder"/> from the named connection string in configuration.
	/// </summary>
	/// <param name="settingName">The connection string name under <c>ConnectionStrings</c> in configuration.</param>
	/// <exception cref="Exception">Thrown when no matching connection string is found.</exception>
	protected SqlConnectionStringBuilder ConnectionStringBuilder(string settingName)
	{
		return new SqlConnectionStringBuilder(Configuration.GetConnectionString(settingName)
			?? throw new Exception($"Cannot find a connection string in the appsettings for {settingName}."));
	}

	/// <summary>
	/// Creates a <see cref="DbConnection"/> for the given connection string.
	/// Override to substitute a different ADO.NET provider or wrap the connection.
	/// </summary>
	/// <param name="connectionString">The fully-resolved connection string.</param>
	protected virtual DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

	/// <summary>
	/// Creates a typed <see cref="DbParameter"/> with the given name, SQL type, and value.
	/// <c>null</c> values are mapped to <see cref="DBNull.Value"/>.
	/// </summary>
	/// <param name="name">The parameter name (e.g. <c>@MyParam</c>).</param>
	/// <param name="type">The SQL Server data type.</param>
	/// <param name="value">The parameter value, or <c>null</c> for SQL NULL.</param>
	protected virtual DbParameter CreateParameter(string name, SqlDbType type, object? value) => new SqlParameter(name, type) { Value = value ?? (object)DBNull.Value };

	/// <summary>
	/// Creates a typed <see cref="DbParameter"/> and passes it to an optional callback before returning.
	/// Useful for setting additional properties such as direction or precision.
	/// </summary>
	/// <param name="name">The parameter name.</param>
	/// <param name="type">The SQL Server data type.</param>
	/// <param name="value">The parameter value, or <c>null</c> for SQL NULL.</param>
	/// <param name="callback">Optional action invoked with the parameter after creation.</param>
	protected virtual DbParameter CreateParameter(string name, SqlDbType type, object? value, Action<DbParameter>? callback)
	{
		var parameter = CreateParameter(name, type, value);
		callback?.Invoke(parameter);
		return parameter;
	}

	/// <summary>
	/// Creates a sized <see cref="DbParameter"/> for fixed-length types such as <c>varchar</c> or <c>nchar</c>.
	/// </summary>
	/// <param name="name">The parameter name.</param>
	/// <param name="type">The SQL Server data type.</param>
	/// <param name="size">The maximum byte/character length.</param>
	/// <param name="value">The parameter value, or <c>null</c> for SQL NULL.</param>
	protected virtual DbParameter CreateParameter(string name, SqlDbType type, int size, object? value) => CreateParameter(name, type, size, value, default);

	/// <summary>
	/// Creates a sized <see cref="DbParameter"/> and passes it to an optional callback before returning.
	/// </summary>
	/// <param name="name">The parameter name.</param>
	/// <param name="type">The SQL Server data type.</param>
	/// <param name="size">The maximum byte/character length.</param>
	/// <param name="value">The parameter value, or <c>null</c> for SQL NULL.</param>
	/// <param name="callback">Optional action invoked with the parameter after creation.</param>
	protected virtual DbParameter CreateParameter(string name, SqlDbType type, int size, object? value, Action<DbParameter>? callback)
	{
		var parameter = CreateParameter(name, type, value);
		parameter.Size = size;
		callback?.Invoke(parameter);
		return parameter;
	}

	/// <summary>
	/// Serializes <paramref name="value"/> to a JSON payload and adds it as a single
	/// <c>nvarchar(max)</c> parameter. Generated code uses this to ship a whole
	/// <c>@Param(s)_*</c> table-valued input as one parameter (shredded server-side by
	/// <c>OPENJSON</c>), avoiding SQL Server's 2100-parameter / 1000-row ceilings.
	/// </summary>
	/// <param name="parameters">The parameter list the new parameter is added to.</param>
	/// <param name="name">The parameter name (e.g. <c>@__json_Params_People</c>).</param>
	/// <param name="value">The list (or one-element array) to serialize.</param>
	protected DbParameter AddJsonParameter(List<DbParameter> parameters, string name, object? value)
	{
		var parameter = CreateParameter(name, SqlDbType.NVarChar, -1, SQuiLJson.Serialize(value));
		parameters.Add(parameter);
		return parameter;
	}
}
