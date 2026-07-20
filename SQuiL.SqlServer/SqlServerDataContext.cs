namespace SQuiL;

using Microsoft.Extensions.Configuration;

/// <summary>
/// SQL Server provider base class for SQuiL-generated data contexts. Carries every
/// Microsoft.Data.SqlClient-specific member; the provider-neutral pieces live on
/// <see cref="SQuiLBaseDataContext"/>. Generated SQL Server data contexts inherit this class.
/// </summary>
/// <param name="Configuration">The <see cref="IConfiguration"/> used to look up connection strings.</param>
public abstract partial class SqlServerDataContext(IConfiguration Configuration)
	: SQuiLBaseDataContext(Configuration)
{
}
