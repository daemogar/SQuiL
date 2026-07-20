namespace SQuiL.Dialects;

using Microsoft.CodeAnalysis;

/// <summary>
/// Maps a <c>SQuiLDialect</c> value to its generator-side <see cref="ISqlDialect"/>, its provider
/// runtime base type (probed on the compilation), and its NuGet package id. Phase 3B registers Sqlite here.
/// </summary>
public static class DialectRegistry
{
	// Fully-qualified metadata name of each dialect's provider runtime base class.
	static string ProviderMetadataNameCore(int dialect) => dialect switch
	{
		0 /* SqlServer */ => "SQuiL.SqlServerDataContext",
		_ => "SQuiL.SqlServerDataContext",
	};

	public static string ProviderMetadataName(int dialect) => ProviderMetadataNameCore(dialect);

	public static string ProviderPackageId(int dialect) => dialect switch
	{
		0 /* SqlServer */ => "SQuiL.SqlServer",
		_ => "SQuiL.SqlServer",
	};

	/// <summary>Friendly dialect name (matches the <c>SQuiLDialect</c> enum member) for diagnostic messages.</summary>
	public static string DialectName(int dialect) => dialect switch
	{
		0 /* SqlServer */ => "SqlServer",
		_ => "SqlServer",
	};

	static ISqlDialect Factory(int dialect) => dialect switch
	{
		0 /* SqlServer */ => new SqlServerDialect(),
		_ => new SqlServerDialect(),
	};

	/// <summary>True when the provider runtime base class for <paramref name="dialect"/> is referenced by the compilation.</summary>
	public static bool IsProviderReferenced(int dialect, Compilation compilation)
		=> compilation.GetTypeByMetadataName(ProviderMetadataName(dialect)) is not null;

	/// <summary>
	/// Resolves the dialect for a data-context class: an explicit choice wins; otherwise the single
	/// referenced provider; otherwise SQL Server (dialect 0). Returns the generator-side dialect object.
	/// </summary>
	public static ISqlDialect Resolve(int? explicitDialect, Compilation compilation)
	{
		if (explicitDialect is int chosen)
			return Factory(chosen);

		// Phase 3A: SqlServer is the only registered provider. When 3B adds Sqlite, replace this
		// with a scan that returns the sole referenced provider, or SqlServer when 0/2+ are present.
		return Factory(0);
	}
}
