using Microsoft.Extensions.Configuration;

using SQuiL;

namespace SQuiL.Tests.SqlServer;

// Real compiled SQL Server data contexts for the live round-trip tests (SqlServerRoundTripTests).
// Each maps one-to-one to a SqlServer/Queries/*.sql file. This .cs sits in the same folder, so each
// QueryFiles member is the bare file name; the "Ms" prefix keeps the names unique against the SQLite
// and PostgreSQL queries, since QueryFiles is one project-wide enum. The explicit [SQuiLDialect] is
// required because this project references all three providers.

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsImportPeople)]
public partial class MsImportPeopleDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsCountPeople)]
public partial class MsCountPeopleDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsImportAddress)]
public partial class MsImportAddressDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsUnicodeRoundTrip)]
public partial class MsUnicodeRoundTripDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsNullRoundTrip)]
public partial class MsNullRoundTripDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsBlobRoundTrip)]
public partial class MsBlobRoundTripDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsPeopleAndCount)]
public partial class MsPeopleAndCountDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsTypedRouting)]
public partial class MsTypedRoutingDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);

[SQuiLDialect(SQuiLDialect.SqlServer)]
[SQuiLQuery(QueryFiles.MsMissingTable)]
public partial class MsMissingTableDataContext(IConfiguration Configuration) : SqlServerDataContext(Configuration);
