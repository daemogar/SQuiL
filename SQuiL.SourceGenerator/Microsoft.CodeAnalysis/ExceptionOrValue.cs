using System.Text;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// A lightweight discriminated union that holds either a successful <typeparamref name="T"/> value
/// or an <see cref="Exception"/>, used throughout the generator to propagate code-generation
/// results without throwing.
/// </summary>
public record ExceptionOrValue<T>
{
	/// <summary><c>true</c> when this instance holds an exception rather than a value.</summary>
	public bool IsException { get; }
	private Exception ExceptionValue { get; } = default!;

	/// <summary>The stored exception; throws <see cref="NullReferenceException"/> if this is a value instance.</summary>
	public Exception Exception => ExceptionValue
		?? throw new NullReferenceException($"Exception is not an Exception");

	/// <summary><c>true</c> when this instance holds a successful value.</summary>
	public bool IsValue { get; }
	private T ValueValue { get; } = default!;

	/// <summary>The stored value; throws <see cref="NullReferenceException"/> if this is an exception instance.</summary>
	public T Value => ValueValue
		?? throw new NullReferenceException($"Value is not type {typeof(T).Name}");

	/// <summary>Creates an exception instance.</summary>
	/// <param name="exception">The exception to store.</param>
	public ExceptionOrValue(Exception exception)
	{
		IsException = true;
		ExceptionValue = exception;
	}

	/// <summary>Creates a value instance.</summary>
	/// <param name="value">The successful result to store.</param>
	public ExceptionOrValue(T value)
	{
		IsValue = true;
		ValueValue = value;
	}

	/// <inheritdoc />
	protected virtual bool PrintMembers(StringBuilder sb)
	{
		if (IsValue)
			sb.Append($"{ValueValue}");
		else
			sb.Append($"""Type = Exception, Message = "{Exception.Message}", StackTrace = {Exception.StackTrace}""");

		return true;
	}

	/// <summary>Attempts to extract the value, discarding any exception.</summary>
	/// <param name="value">Set to the stored value when returning <c>true</c>.</param>
	/// <returns><c>true</c> if this instance holds a value.</returns>
	public bool TryGetValue(out T value) => TryGetValue(out value, out var _);

	/// <summary>Attempts to extract the value, also exposing the exception on failure.</summary>
	/// <param name="value">Set to the stored value when returning <c>true</c>.</param>
	/// <param name="exception">Set to the stored exception when returning <c>false</c>.</param>
	/// <returns><c>true</c> if this instance holds a value.</returns>
	public bool TryGetValue(out T value, out Exception exception)
	{
		value = ValueValue;
		exception = ExceptionValue;

		return IsValue;
	}

	/// <summary>Wraps an error message string as an exception instance.</summary>
	public static implicit operator ExceptionOrValue<T>(string statement) => new(new Exception(statement));
	/// <summary>Wraps an exception as an exception instance.</summary>
	public static implicit operator ExceptionOrValue<T>(Exception statement) => new(statement);
	/// <summary>Wraps a value as a value instance.</summary>
	public static implicit operator ExceptionOrValue<T>(T statement) => new(statement);
}