namespace System;

public static class StringExtensions
{
	public static bool IsNullOrWhiteSpace(this string text)
		=> string.IsNullOrWhiteSpace(text);
}