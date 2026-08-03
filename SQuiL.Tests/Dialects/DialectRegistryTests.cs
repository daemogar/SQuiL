namespace SQuiL.Tests.Dialects;

using global::SQuiL;
using global::SQuiL.Dialects;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Xunit;

public class DialectRegistryTests
{
	static Compilation CompilationWithProvider() =>
		CSharpCompilation.Create("probe",
			references: [MetadataReference.CreateFromFile(typeof(SqlServerDataContext).Assembly.Location)]);

	static Compilation CompilationWithoutProvider() =>
		CSharpCompilation.Create("probe", references: []);

	[Fact]
	public void Explicit_SqlServer_resolves_to_SqlServerDialect()
	{
		var dialect = DialectRegistry.Resolve((int)SQuiLDialect.SqlServer, CompilationWithProvider());
		Assert.Equal("SqlServerDataContext", dialect.RuntimeBaseType());
	}

	[Fact]
	public void No_explicit_dialect_defaults_to_SqlServer()
	{
		var dialect = DialectRegistry.Resolve(null, CompilationWithProvider());
		Assert.Equal("SqlServerDataContext", dialect.RuntimeBaseType());
	}

	[Fact]
	public void Provider_present_is_detected()
	{
		Assert.True(DialectRegistry.IsProviderReferenced((int)SQuiLDialect.SqlServer, CompilationWithProvider()));
	}

	[Fact]
	public void Provider_absent_is_detected()
	{
		Assert.False(DialectRegistry.IsProviderReferenced((int)SQuiLDialect.SqlServer, CompilationWithoutProvider()));
	}

	[Fact]
	public void Resolve_explicit_sqlite_returns_sqlite_dialect()
	{
		var compilation = CompilationWithoutProvider();
		var dialect = DialectRegistry.Resolve((int)SQuiLDialect.Sqlite, compilation);
		Assert.Equal("SqliteDataContext", dialect.RuntimeBaseType());
		Assert.Equal("SQuiL.Sqlite", DialectRegistry.ProviderPackageId((int)SQuiLDialect.Sqlite));
		Assert.Equal("Sqlite", DialectRegistry.DialectName((int)SQuiLDialect.Sqlite));
	}

	[Fact]
	public void Resolve_explicit_postgres_returns_postgres_dialect()
	{
		var compilation = CompilationWithoutProvider();
		var dialect = DialectRegistry.Resolve((int)SQuiLDialect.Postgres, compilation);
		Assert.Equal("PostgresDataContext", dialect.RuntimeBaseType());
		Assert.Equal("SQuiL.Postgres", DialectRegistry.ProviderPackageId((int)SQuiLDialect.Postgres));
		Assert.Equal("Postgres", DialectRegistry.DialectName((int)SQuiLDialect.Postgres));
	}

	[Fact]
	public void Factory_postgres_returns_PostgresDialect_instance()
	{
		Assert.IsType<PostgresDialect>(DialectRegistry.Factory((int)SQuiLDialect.Postgres));
	}

	[Fact]
	public void ProviderMetadataName_postgres_is_fully_qualified_PostgresDataContext()
	{
		Assert.Equal("SQuiL.PostgresDataContext", DialectRegistry.ProviderMetadataName((int)SQuiLDialect.Postgres));
	}
}
