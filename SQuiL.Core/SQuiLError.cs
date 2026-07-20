namespace SQuiL;

using System;
using System.Data.Common;

/// <summary>
/// Represents a single database error captured during query execution.
/// The six fields mirror the classic SQL Server error shape and are provider-neutral data.
/// </summary>
/// <param name="Number">The database error number (e.g. 2627 for a unique-constraint violation).</param>
/// <param name="Severity">The error severity level (0–10 = informational, 11–16 = user errors, 17–25 = system errors).</param>
/// <param name="State">The error state — used by SQL Server to pinpoint the location within the procedure that raised the error.</param>
/// <param name="Line">The line number in the batch or stored procedure where the error occurred.</param>
/// <param name="Procedure">The name of the stored procedure or trigger that raised the error, or an empty string for ad-hoc batches.</param>
/// <param name="Message">The human-readable error message text.</param>
public partial record SQuiLError(
	int Number,
	int Severity,
	int State,
	int Line,
	string Procedure,
	string Message)
{
	private DbException? Exception { get; init; }

	/// <summary>Internal: attach the underlying provider exception (used by the provider's CreateError seam).</summary>
	internal SQuiLError WithException(DbException? exception) => this with { Exception = exception };

	/// <summary>Wraps this error's message in a plain <see cref="Exception"/>.</summary>
	public Exception AsException() => new(Message);

	/// <summary>Wraps this error in a <see cref="SQuiLException"/>, preserving all error fields.</summary>
	public SQuiLException AsSQuiLException() => new(this);

	/// <summary>Gets the underlying provider exception if one was captured, else null.</summary>
	public DbException? AsDbException() => Exception;
}
