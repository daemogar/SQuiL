namespace SQuiL;

/// <summary>The database dialect a SQuiL data context targets. Selects the provider runtime base class and the SQL the generator emits.</summary>
public enum SQuiLDialect
{
	/// <summary>Microsoft SQL Server (provider package <c>SQuiL.SqlServer</c>). The default.</summary>
	SqlServer,

	/// <summary>SQLite (provider package <c>SQuiL.Sqlite</c>).</summary>
	Sqlite,

	/// <summary>PostgreSQL (via Npgsql).</summary>
	Postgres = 2,
}
