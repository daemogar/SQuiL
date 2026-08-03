using Microsoft.Extensions.Configuration;

using SQuiL;

namespace SQuiL.Tests.Postgres;

// Real compiled PostgreSQL data contexts for the Task 9 end-to-end LIVE round-trip tests. Each maps
// one-to-one to a Postgres\Queries\*.sql file (the QueryFiles enum member is the bare file name — the
// "Pg" prefix on every file keeps these globally unique against the pre-existing Sqlite\Queries\*.sql
// names, since QueryFiles is one project-wide enum). All are [SQuiLDialect(SQuiLDialect.Postgres)] so
// the generator emits the PostgreSQL runtime base + json_to_recordset shred + Npgsql reader
// accessors. These live in the compiled test assembly (the generator runs here as an Analyzer),
// exercised against a REAL postgres:17 container (see PostgresContainerFixture), NOT the in-memory
// snapshot harness the other Postgres* test classes use.

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgImportPeople)]
public partial class PgImportPeopleDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgImportAddress)]
public partial class PgImportAddressDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgCountPeople)]
public partial class PgCountPeopleDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgNullRoundTrip)]
public partial class PgNullRoundTripDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgBlobRoundTrip)]
public partial class PgBlobRoundTripDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgPeopleAndCount)]
public partial class PgPeopleAndCountDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgTypedRouting)]
public partial class PgTypedRoutingDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Postgres)]
[SQuiLQuery(QueryFiles.PgMissingTable)]
public partial class PgMissingTableDataContext(IConfiguration Configuration) : PostgresDataContext(Configuration);
