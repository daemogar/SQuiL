namespace SQuiL;

using System;

/// <summary>
/// Optional. Declares which <see cref="SQuiLDialect"/> a data-context class targets.
/// When absent, the generator uses the single referenced provider, or SQL Server by default.
/// </summary>
/// <param name="Dialect">The dialect this data context targets.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SQuiLDialectAttribute(SQuiLDialect Dialect) : Attribute
{
	/// <summary>The dialect this data context targets.</summary>
	public SQuiLDialect Dialect { get; } = Dialect;
}
