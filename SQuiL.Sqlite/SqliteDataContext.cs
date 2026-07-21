namespace SQuiL;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Data.Common;

/// <summary>
/// SQLite runtime base for generated data contexts. Sibling of <c>SqlServerDataContext</c>;
/// exposes the same member NAMES the generated code calls, over Microsoft.Data.Sqlite.
/// </summary>
/// <param name="configuration">The <see cref="IConfiguration"/> used to look up connection strings.</param>
public abstract partial class SqliteDataContext(IConfiguration configuration)
	: SQuiLBaseDataContext(configuration)
{
	/// <summary>
	/// Builds a <see cref="SqliteConnectionStringBuilder"/> from the named connection string in configuration.
	/// </summary>
	/// <param name="settingName">The connection string name under <c>ConnectionStrings</c> in configuration.</param>
	/// <exception cref="InvalidOperationException">Thrown when no matching connection string is found.</exception>
	protected SqliteConnectionStringBuilder ConnectionStringBuilder(string settingName)
		=> new(Configuration.GetConnectionString(settingName)
			?? throw new InvalidOperationException(
				$"No connection string named '{settingName}' was found in configuration."));

	/// <summary>Creates (does not open) a SQLite connection.</summary>
	protected virtual DbConnection CreateConnection(string connectionString)
		=> new SqliteConnection(connectionString);

	/// <summary>Creates a typed SQLite parameter (null -> DBNull).</summary>
	protected virtual DbParameter CreateParameter(string name, SqliteType type, object? value)
		=> new SqliteParameter(name, type) { Value = value ?? DBNull.Value };

	/// <summary>Creates a typed SQLite parameter with post-configuration.</summary>
	protected virtual DbParameter CreateParameter(string name, SqliteType type, object? value, Action<DbParameter>? configure)
	{
		var parameter = new SqliteParameter(name, type) { Value = value ?? DBNull.Value };
		configure?.Invoke(parameter);
		return parameter;
	}

	/// <summary>
	/// Serialises <paramref name="value"/> to JSON and adds it as a TEXT parameter. The generated
	/// SQLite shred reads it with <c>json_each</c> / <c>json_extract</c>.
	/// </summary>
	protected DbParameter AddJsonParameter(List<DbParameter> parameters, string name, object? value)
	{
		var parameter = CreateParameter(name, SqliteType.Text, SQuiLJson.Serialize(value));
		parameters.Add(parameter);
		return parameter;
	}

	/// <summary>Maps a <see cref="SqliteException"/> to the neutral <see cref="SQuiLError"/>.</summary>
	protected SQuiLError CreateError(SqliteException e)
		=> new SQuiLError(e.SqliteErrorCode, 0, e.SqliteExtendedErrorCode, 0, string.Empty, e.Message)
			.WithException(e);
}
