namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill required for C# 9 <c>init</c> setters and record types when targeting
/// <c>netstandard2.0</c>, which does not define this type.
/// </summary>
public static class IsExternalInit { }
