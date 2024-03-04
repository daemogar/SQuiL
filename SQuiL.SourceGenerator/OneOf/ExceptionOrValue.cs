using System.Text;

namespace Microsoft.CodeAnalysis;

public record ExceptionOrValue<T>
{
	public bool IsException { get; }
	private Exception ExceptionValue { get; } = default!;
	public Exception Exception => ExceptionValue
		?? throw new NullReferenceException($"Exception is not an Exception");

	public bool IsValue { get; }
	private T ValueValue { get; } = default!;
	public T Value => ValueValue
		?? throw new NullReferenceException($"Value is not type {typeof(T).Name}");

	public ExceptionOrValue(Exception exception)
	{
		IsException = true;
		ExceptionValue = exception;
	}

	public ExceptionOrValue(T value)
	{
		IsValue = true;
		ValueValue = value;
	}

	protected virtual bool PrintMembers(StringBuilder sb)
	{
		if (IsValue)
			sb.Append($"{ValueValue}");
		else
			sb.Append($"""Type = Exception, Message = "{Exception.Message}", StackTrace = {Exception.StackTrace}""");

		return true;
	}

	public bool TryGetValue(out T value) => TryGetValue(out value, out var _);

	public bool TryGetValue(out T value, out Exception exception)
	{
		value = ValueValue;
		exception = ExceptionValue;

		return IsValue;
	}

	public static implicit operator ExceptionOrValue<T>(string statement) => new(new Exception(statement));
	public static implicit operator ExceptionOrValue<T>(Exception statement) => new(statement);
	public static implicit operator ExceptionOrValue<T>(T statement) => new(statement);
}