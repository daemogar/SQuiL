namespace SQuiL;

using System;

/// <summary>
/// Generated marker naming an assembly's SQuiL registration method, so a referencing project's
/// <c>AddSQuiL()</c> can call it. Emitted by the generator; not for hand use.
/// </summary>
/// <param name="Extensions">The generated static class that holds the method.</param>
/// <param name="Method">The registration method's name.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class SQuiLRegistrationAttribute(Type Extensions, string Method) : Attribute
{
	/// <summary>The generated static class that holds the method.</summary>
	public Type Extensions { get; } = Extensions;

	/// <summary>The registration method's name.</summary>
	public string Method { get; } = Method;
}
