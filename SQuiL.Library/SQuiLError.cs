namespace SQuiL;

using Microsoft.Data.SqlClient;

using System;

/// <summary>
/// Represents a single SQL Server error or informational message captured during query execution.
/// Mirrors the fields exposed by <c>SqlError</c> in the ADO.NET error collection.
/// </summary>
/// <param name="Number">The SQL Server error number (e.g. 2627 for a unique-constraint violation).</param>
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
	private SqlException? Exception { get; }

	/// <summary>
	/// Represents a single SQL Server exception captured during query execution.
	/// Mirrors the fields exposed by <c>SqlError</c> in the ADO.NET error collection.
	/// </summary>
	/// <param name="exception">The SQL Server exception captured during query execution.</param>
	public SQuiLError(SqlException exception)
		: this(exception.Number, exception.Class, exception.State, exception.LineNumber, exception.Procedure, exception.Message)
	{
		Exception = exception;
	}

	/// <summary>Wraps this error's message in a plain <see cref="Exception"/>.</summary>
	public Exception AsException() => new(Message);

	/// <summary>Wraps this error in a <see cref="SQuiLException"/>, preserving all SQL error fields.</summary>
	public SQuiLException AsSQuiLException() => new(this);

	/// <summary>Get the underlying SQL exception if one exists for this error.</summary>
	public SqlException? AsSqlException() => Exception;
}
