namespace SQuiL;

using Npgsql;
using NpgsqlTypes;

using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Data.Common;

/// <summary>
/// PostgreSQL runtime base for generated data contexts. Sibling of <c>SqlServerDataContext</c>
/// and <c>SqliteDataContext</c>; exposes the same member NAMES the generated code calls, over Npgsql.
/// </summary>
/// <param name="configuration">The <see cref="IConfiguration"/> used to look up connection strings.</param>
public abstract partial class PostgresDataContext(IConfiguration configuration)
	: SQuiLBaseDataContext(configuration)
{
	/// <summary>Builds an <see cref="NpgsqlConnectionStringBuilder"/> from the named connection string.</summary>
	protected NpgsqlConnectionStringBuilder ConnectionStringBuilder(string settingName)
		=> new(Configuration.GetConnectionString(settingName)
			?? throw new InvalidOperationException(
				$"No connection string named '{settingName}' was found in configuration."));

	/// <summary>Creates (does not open) an Npgsql connection.</summary>
	protected virtual DbConnection CreateConnection(string connectionString)
		=> new NpgsqlConnection(connectionString);

	/// <summary>Creates a typed Npgsql parameter (null -> DBNull).</summary>
	protected virtual DbParameter CreateParameter(string name, NpgsqlDbType type, object? value)
		=> new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };

	/// <summary>Creates a typed Npgsql parameter with post-configuration.</summary>
	protected virtual DbParameter CreateParameter(string name, NpgsqlDbType type, object? value, Action<DbParameter>? configure)
	{
		var parameter = new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value };
		configure?.Invoke(parameter);
		return parameter;
	}

	/// <summary>
	/// Serialises <paramref name="value"/> to JSON and adds it as a text parameter. The generated
	/// PostgreSQL shred reads it with <c>json_to_recordset</c>.
	/// </summary>
	protected DbParameter AddJsonParameter(List<DbParameter> parameters, string name, object? value)
	{
		var parameter = CreateParameter(name, NpgsqlDbType.Text, SQuiLJson.Serialize(value));
		parameters.Add(parameter);
		return parameter;
	}

	/// <summary>Maps an <see cref="NpgsqlException"/> to the neutral <see cref="SQuiLError"/>.</summary>
	protected SQuiLError CreateError(NpgsqlException e)
		=> new SQuiLError(e.ErrorCode, 0, 0, 0, string.Empty, e.Message).WithException(e);

	/// <summary>
	/// PostgreSQL dialect: provider type name (as reported by Npgsql's
	/// <c>DbDataReader.GetDataTypeName</c>) -> canonical C# routing token (matching Token.CSharpType).
	/// Length/precision ignored. Unknown types pass through lower-cased (clean skip). MUST stay in
	/// parity with the build-time key (see KeyParityTests, which derives the provider type name from a
	/// LIVE Npgsql reader — not a hand-fed literal). Unlike SQLite, PostgreSQL reports distinct type
	/// names for boolean/uuid/timestamp/date, so no affinity coarsening is needed — this map is the
	/// PG spelling of the SQL Server map.
	/// </summary>
	protected override string NormalizeType(string providerTypeName) => providerTypeName.ToLowerInvariant() switch
	{
		"integer" or "int4" or "int" => "int",
		"bigint" or "int8" => "long",
		"smallint" or "int2" => "short",
		"text" or "character varying" or "varchar" or "character" or "char" or "bpchar" or "json" or "jsonb" => "string",
		"numeric" or "decimal" or "money" => "decimal",
		"boolean" or "bool" => "bool",
		"uuid" => "System.Guid",
		"date" => "System.DateOnly",
		"time without time zone" or "time" => "System.TimeOnly",
		"timestamp without time zone" or "timestamp" => "System.DateTime",
		"timestamp with time zone" or "timestamptz" => "System.DateTimeOffset",
		"bytea" => "byte[]",
		"real" or "float4" => "float",
		"double precision" or "float8" => "double",
		var other => other,
	};
}
