namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill that enables the C# range-slicing operator (<c>array[x..y]</c>) on
/// <c>netstandard2.0</c> targets. The compiler emits a call to <see cref="GetSubArray{T}"/>
/// whenever a range expression is applied to an array; this class provides that implementation
/// on runtimes that predate <c>net5.0</c>.
/// </summary>
public static class RuntimeHelpers
{
	/// <summary>
	/// Slices the specified array using the specified range.
	/// </summary>
	public static T[] GetSubArray<T>(T[] array, Range range)
	{
		if (array == null)
			throw new ArgumentNullException(nameof(array));
		
		(int offset, int length) = range.GetOffsetAndLength(array.Length);

		if (default(T) != null || typeof(T[]) == array.GetType())
		{
			// We know the type of the array to be exactly T[].

			if (length == 0)
				return [];
			
			var dest = new T[length];
			Array.Copy(array, offset, dest, 0, length);
			return dest;
		}
		else
		{
			// The array is actually a U[] where U:T.
			var dest = (T[])Array.CreateInstance(array.GetType().GetElementType(), length);
			Array.Copy(array, offset, dest, 0, length);
			return dest;
		}
	}
}
