namespace SQuiL;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data;

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
	/// Appends a positional table-valued parameter placeholder to <paramref name="query"/> and
	/// adds the corresponding <see cref="DbParameter"/> to <paramref name="parameters"/>.
	/// Used by generated code when expanding <c>@Params_*</c> table variables into individual row parameters.
	/// </summary>
	/// <param name="query">The SQL query being built.</param>
	/// <param name="parameters">The parameter list the new parameter is added to.</param>
	/// <param name="index">The zero-based row index within the table variable.</param>
	/// <param name="table">The table variable name (without the <c>@</c> prefix).</param>
	/// <param name="name">The column/property name within the row.</param>
	/// <param name="type">The SQL Server data type of the column.</param>
	/// <param name="value">The column value for this row, or <c>null</c> for SQL NULL.</param>
	/// <param name="size">Maximum string length; 0 means no size constraint is applied.</param>
	/// <exception cref="Exception">Thrown when a non-null string value exceeds <paramref name="size"/>.</exception>
	protected void AddParams(System.Text.StringBuilder query, List<DbParameter> parameters, int index, string table, string name, SqlDbType type, object? value, int size = 0)
	{
		var parameter = $"@{table}_{index}_{name}";
		query.Append(parameter);

		var variable = CreateParameter(parameter, type, value);

		if (size > 0)
		{
			variable.Size = size;
			variable.Value = value is null || ((string)value).Length <= size
				? (value ?? "Null")
				: throw new Exception($"""
					ParamsTable model table property at index [{index}] has a string property [{name}]
					with more than {size} characters.
					""");
		}

		parameters.Add(variable);
	}
}
