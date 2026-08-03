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

	/// <summary>
	/// SQLite dialect: provider type name (the DECLARED decltype as reported verbatim, parens
	/// stripped, by <c>Microsoft.Data.Sqlite</c>'s <c>DbDataReader.GetDataTypeName</c>) -> canonical
	/// C# routing token. MUST stay in parity with the build-time routing token
	/// <c>SQuiLShapeKey.RoutingType</c> emits for SQLite-dialect blocks (see
	/// <c>SQuiL.Tests.ShapeDetection.KeyParityTests</c>, which derives the provider type name from a
	/// LIVE reader — not a hand-fed affinity string).
	/// <para>
	/// The generated <c>Create Temp Table</c> preserves the author's verbatim type spelling
	/// (<c>Token.Original</c>), so <c>GetDataTypeName</c> returns that spelling — NOT a storage-class
	/// affinity. This map therefore enumerates EVERY decltype spelling the SQLite tokenizer accepts
	/// for the eight token types <c>SqliteDialect</c> supports with a typed reader accessor
	/// (<c>SQuiLTokenizer</c> <c>TypeRegex</c> + <c>SqliteTypeRegex</c>), each collapsed to the SAME
	/// coarsened token the build side emits:
	/// </para>
	/// <list type="bullet">
	///   <item>TYPE_BIGINT (<c>integer</c>/<c>bigint</c>) -&gt; <c>long</c></item>
	///   <item>TYPE_STRING (<c>text</c>/<c>ntext</c>/<c>char</c>/<c>nchar</c>/<c>varchar</c>/<c>nvarchar</c>) -&gt; <c>string</c></item>
	///   <item>TYPE_DOUBLE (<c>real</c>/<c>float</c>) -&gt; <c>double</c> (author's <c>double</c> is emitted as <c>float</c>)</item>
	///   <item>TYPE_VARBINARY (<c>blob</c>/<c>varbinary</c>) -&gt; <c>byte[]</c></item>
	///   <item>TYPE_DECIMAL (<c>numeric</c>/<c>decimal</c>) -&gt; <c>decimal</c></item>
	///   <item>TYPE_BOOLEAN (<c>boolean</c>/<c>bit</c>) -&gt; <c>long</c> (INTEGER affinity)</item>
	///   <item>TYPE_DATETIME (<c>date</c>/<c>datetime</c>/<c>datetime2</c>) -&gt; <c>string</c> (TEXT affinity)</item>
	///   <item>TYPE_GUID (<c>guid</c>/<c>uniqueidentifier</c>) -&gt; <c>string</c> (TEXT affinity)</item>
	/// </list>
	/// Unrecognized types pass through lower-cased so they simply fail to match any declared output
	/// (clean skip).
	/// </summary>
	protected override string NormalizeType(string providerTypeName) => providerTypeName.ToLowerInvariant() switch
	{
		// TYPE_BIGINT -> long
		"integer" or "bigint" => "long",
		// TYPE_STRING -> string
		"text" or "ntext" or "char" or "nchar" or "varchar" or "nvarchar" => "string",
		// TYPE_DOUBLE -> double (an authored `double` is emitted as `float`)
		"real" or "float" => "double",
		// TYPE_VARBINARY -> byte[]
		"blob" or "varbinary" => "byte[]",
		// TYPE_DECIMAL -> decimal
		"numeric" or "decimal" => "decimal",
		// TYPE_BOOLEAN -> long (SQLite INTEGER affinity; build RoutingType coarsens to "long")
		"boolean" or "bit" => "long",
		// TYPE_DATETIME -> string (SQLite TEXT affinity; build RoutingType coarsens to "string")
		"date" or "datetime" or "datetime2" => "string",
		// TYPE_GUID -> string (SQLite TEXT affinity; build RoutingType coarsens to "string")
		"guid" or "uniqueidentifier" => "string",
		var other => other,
	};
}
