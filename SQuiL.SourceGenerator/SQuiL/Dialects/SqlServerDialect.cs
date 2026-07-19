namespace SQuiL.Dialects;

/// <summary>
/// The SQL Server dialect: the single source of every SQL-Server-specific string the
/// generator bakes into emitted C# and SQL. Phase 1 has one concrete dialect; Phase 2
/// extracts an <c>ISqlDialect</c> interface from this surface and adds SQLite.
/// </summary>
public class SqlServerDialect
{
	/// <summary>The provider using-directive emitted into every generated data-context file.</summary>
	public string ProviderUsingDirective() => "using Microsoft.Data.SqlClient;";

	/// <summary>The provider exception type caught in the generated execute/read try-blocks.</summary>
	public string ProviderExceptionType() => "SqlException";
}
