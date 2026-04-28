namespace System;

/// <summary>Polyfill extension methods on <see cref="string"/> for targets that lack them.</summary>
public static class StringExtensions
{
	/// <summary>Returns <c>true</c> if <paramref name="text"/> is <c>null</c>, empty, or whitespace-only.</summary>
	public static bool IsNullOrWhiteSpace(this string text)
		=> string.IsNullOrWhiteSpace(text);
}