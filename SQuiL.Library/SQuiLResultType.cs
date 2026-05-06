namespace SQuiL;

using System.Collections.Generic;
using System.Runtime.CompilerServices;

public sealed record SQuiLResultType
{
	/// <summary>
	/// Return result type that has no value and no errors.
	/// </summary>
	public static SQuiLResultType Success { get; } = new();

	/// <summary>Constructs a successful result wrapping <paramref name="value"/>.</summary>
	/// <param name="value">The query response model.</param>
	private SQuiLResultType() { }

	/// <summary><c>true</c> when the query returned one or more SQL errors.</summary>
	public bool HasErrors { get; }

	private IReadOnlyList<SQuiLError> Errors { get; } = default!;

	/// <summary>Constructs a failed result wrapping the SQL error reported by the query.</summary>
	/// <param name="errors">The error returned by the query.</param>
	public SQuiLResultType(SQuiLError error) : this([error]) { }

	/// <summary>Constructs a failed result wrapping the SQL errors reported by the query.</summary>
	/// <param name="errors">The non-empty list of errors returned by the query.</param>
	public SQuiLResultType(IReadOnlyList<SQuiLError> errors)
	{
		Errors = errors;
		HasErrors = true;
	}

	/// <summary>
	/// Attempts to extract the errors.
	/// </summary>
	/// <param name="errors">Set to the error list when the result has errors; otherwise <c>default</c>.</param>
	/// <returns><c>true</c> if the result holds no errors; <c>false</c> if it holds errors.</returns>
	public bool TryGetErrors(out IReadOnlyList<SQuiLError> errors)
	{
		errors = default!;

		if (!HasErrors)
			return false;

		errors = Errors;
		return true;
	}
}

/// <summary>
/// Represents the result of a SQuiL query execution — either a successful value of type
/// <typeparamref name="T"/> or a list of SQL errors returned by the query.
/// Exactly one of <see cref="IsValue"/> or <see cref="HasErrors"/> will be <c>true</c>.
/// </summary>
/// <typeparam name="T">The response model type produced by the query on success.</typeparam>
public sealed record SQuiLResultType<T>
{
	/// <summary><c>true</c> when the query completed successfully and a value is available.</summary>
	public bool IsValue { get; }

	private T Value { get; } = default!;

	/// <summary>Constructs a successful result wrapping <paramref name="value"/>.</summary>
	/// <param name="value">The query response model.</param>
	public SQuiLResultType(T value)
	{
		Value = value;
		IsValue = true;
	}

	/// <summary><c>true</c> when the query returned one or more SQL errors.</summary>
	public bool HasErrors { get; }

	private IReadOnlyList<SQuiLError> Errors { get; } = default!;

	/// <summary>Constructs a failed result wrapping the SQL error reported by the query.</summary>
	/// <param name="errors">The error returned by the query.</param>
	public SQuiLResultType(SQuiLError error) : this([error]) { }

	/// <summary>Constructs a failed result wrapping the SQL errors reported by the query.</summary>
	/// <param name="errors">The non-empty list of errors returned by the query.</param>
	public SQuiLResultType(IReadOnlyList<SQuiLError> errors)
	{
		Errors = errors;
		HasErrors = true;
	}

	/// <summary>
	/// Attempts to extract the success value.
	/// </summary>
	/// <param name="value">Set to the query response when the result is successful; otherwise <c>default</c>.</param>
	/// <param name="errors">Set to the error list when the result has errors; otherwise <c>default</c>.</param>
	/// <returns><c>true</c> if the result holds a value; <c>false</c> if it holds errors.</returns>
	public bool TryGetValue(out T value, out IReadOnlyList<SQuiLError> errors)
	{
		value = default!;
		errors = default!;

		if (IsValue)
		{
			value = Value;
			return true;
		}

		errors = Errors;
		return false;
	}
}
