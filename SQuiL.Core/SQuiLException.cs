namespace SQuiL;

using Microsoft.Data.SqlClient;

using System;
using System.Collections.Generic;
using System.Data.Common;

/// <summary>
/// A <see cref="DbException"/> that carries the full details of a single SQL Server error.
/// Thrown by generated data contexts when a query returns a non-recoverable SQL error
/// and the caller has not opted into the <see cref="SQuiLResultType{T}"/> error-list pattern.
/// </summary>
public sealed class SQuiLException : DbException
{
	/// <summary>
	/// Initialises the exception from a <see cref="SQuiLError"/>.
	/// The base exception message is set to <see cref="SQuiLError.Message"/> and
	/// <see cref="Exception.HResult"/> is set to the SQL error number.
	/// </summary>
	/// <param name="error">The SQL error that caused this exception.</param>
	public SQuiLException(SQuiLError error) : base(error.Message)
	{
		Error = error;
		HResult = error.Number;
	}

	/// <summary>The original SQL error record that produced this exception.</summary>
	private SQuiLError Error { get; }

	/// <inheritdoc cref="SQuiLError.AsSqlException"/>
	public SqlException? Exception => Error.AsSqlException();

	/// <inheritdoc/>
	public override Exception GetBaseException() => this;

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> Error.Equals(obj is SQuiLException e ? e.Error : obj);

	/// <inheritdoc/>
	public override int GetHashCode() => Error.GetHashCode();

	/// <summary>Link to the SQuiL project repository.</summary>
	public override string? HelpLink => "https://github.com/daemogar/SQuiL";

	/// <summary>
	/// Returns a formatted string containing the exception type, HRESULT, message,
	/// SQL error metadata (number, severity, state, procedure, line), and stack trace.
	/// </summary>
	public override string ToString()
	{
		System.Text.StringBuilder sb = new();

		sb.AppendFormat($"{GetType().FullName} (0x{HResult:X8}): {Message}");

		sb.AppendLine();

		sb.AppendFormat($"   Number: {Error.Number}, Severity: {Error.Severity}, State: {Error.State}");

		if (!string.IsNullOrWhiteSpace(Error.Procedure))
			sb.AppendFormat($", Procedure: {Error.Procedure}");

		sb.AppendFormat($", Line {Error.Line}");

		var trace = StackTrace;
		if (trace is not null)
		{
			sb.AppendLine();
			sb.Append(trace);
		}

		return sb.ToString();
	}

	/// <summary>
	/// Returns the SQL error fields as a dictionary keyed by field name.
	/// Keys: <c>Number</c>, <c>Severity</c>, <c>State</c>, <c>Line</c>, <c>Procedure</c>, <c>Message</c>.
	/// </summary>
	public override System.Collections.IDictionary Data
		=> new Dictionary<string, object>()
		{
			{ "Number", Error.Number },
			{ "Severity", Error.Severity },
			{ "State", Error.State },
			{ "Line", Error.Line },
			{ "Procedure", Error.Procedure },
			{ "Message", Error.Message }
		};

	/// <inheritdoc/>
	public override string? StackTrace => base.StackTrace;

	/// <summary>Always returns <c>"SQuiL"</c>.</summary>
	public override string? Source => "SQuiL";
}
