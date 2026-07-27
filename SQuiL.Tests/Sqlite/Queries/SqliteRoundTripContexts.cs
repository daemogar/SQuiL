using Microsoft.Extensions.Configuration;

using SQuiL;

namespace SQuiL.Tests.Sqlite;

// Real compiled SQLite data contexts for the Task 8 end-to-end round-trip tests. Each maps
// one-to-one to a Sqlite\Queries\*.sql file (the QueryFiles enum member is the bare file name —
// this .cs living in the SAME directory as the .sql makes the generator's path-flattening strip
// down to it). All are [SQuiLDialect(SQuiLDialect.Sqlite)] so the generator emits the SQLite
// runtime base + json_each shred + SQLite reader accessors. These live in the compiled test
// assembly (the generator runs here as an Analyzer), NOT the in-memory snapshot harness.

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.ImportPeople)]
public partial class ImportPeopleDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.NullRoundTrip)]
public partial class NullRoundTripDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.BlobRoundTrip)]
public partial class BlobRoundTripDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.CountPeople)]
public partial class CountPeopleDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.PeopleAndCount)]
public partial class PeopleAndCountDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.Sqlite)]
[SQuiLQuery(QueryFiles.MissingTable)]
public partial class MissingTableDataContext(IConfiguration Configuration) : SqliteDataContext(Configuration);
