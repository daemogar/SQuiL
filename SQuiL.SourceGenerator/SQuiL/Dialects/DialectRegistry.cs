namespace SQuiL.Dialects;

using Microsoft.CodeAnalysis;

using System.Collections.Generic;

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
		1 /* Sqlite    */ => "SQuiL.SqliteDataContext",
		_ => "SQuiL.SqlServerDataContext",
	};

	public static string ProviderMetadataName(int dialect) => ProviderMetadataNameCore(dialect);

	public static string ProviderPackageId(int dialect) => dialect switch
	{
		0 /* SqlServer */ => "SQuiL.SqlServer",
		1 /* Sqlite    */ => "SQuiL.Sqlite",
		_ => "SQuiL.SqlServer",
	};

	/// <summary>Friendly dialect name (matches the <c>SQuiLDialect</c> enum member) for diagnostic messages.</summary>
	public static string DialectName(int dialect) => dialect switch
	{
		0 /* SqlServer */ => "SqlServer",
		1 /* Sqlite    */ => "Sqlite",
		_ => "SqlServer",
	};

	public static ISqlDialect Factory(int dialect) => dialect switch
	{
		0 /* SqlServer */ => new SqlServerDialect(),
		1 /* Sqlite    */ => new SqliteDialect(),
		_ => new SqlServerDialect(),
	};

	/// <summary>True when the provider runtime base class for <paramref name="dialect"/> is referenced by the compilation.</summary>
	public static bool IsProviderReferenced(int dialect, Compilation compilation)
		=> compilation.GetTypeByMetadataName(ProviderMetadataName(dialect)) is not null;

	/// <summary>Dialect ids whose provider runtime base type is referenced by the compilation.</summary>
	public static IReadOnlyList<int> ReferencedProviderIds(Compilation compilation)
	{
		var found = new List<int>();
		foreach (var id in new[] { 0, 1 }) // SqlServer, Sqlite
			if (IsProviderReferenced(id, compilation))
				found.Add(id);
		return found;
	}

	/// <summary>Sentinel returned by <see cref="ResolveId"/> when 2+ providers are referenced with no explicit dialect.</summary>
	public const int Ambiguous = -1;

	/// <summary>
	/// Resolves the dialect id for a context: an explicit choice wins; else the single referenced
	/// provider; else SqlServer (0). Returns <see cref="Ambiguous"/> when 2+ providers are referenced
	/// and no explicit dialect was given.
	/// </summary>
	public static int ResolveId(int? explicitDialect, Compilation compilation)
	{
		if (explicitDialect is int chosen)
			return chosen;

		var referenced = ReferencedProviderIds(compilation);
		return referenced.Count switch
		{
			1 => referenced[0],
			0 => 0,            // SqlServer default; SP0038 fires later if its provider is absent
			_ => Ambiguous,
		};
	}

	/// <summary>
	/// Resolves the dialect for a data-context class: an explicit choice wins; else the single
	/// referenced provider package; else SQL Server (dialect 0) is the default. Returns the
	/// generator-side dialect object. An ambiguous resolution (2+ providers referenced, no explicit
	/// choice) falls back to SqlServer here — callers that need to report SP0039 on ambiguity should
	/// call <see cref="ResolveId"/> directly and check against <see cref="Ambiguous"/> themselves.
	/// </summary>
	public static ISqlDialect Resolve(int? explicitDialect, Compilation compilation)
	{
		var id = ResolveId(explicitDialect, compilation);
		return Factory(id == Ambiguous ? 0 : id);
	}
}
