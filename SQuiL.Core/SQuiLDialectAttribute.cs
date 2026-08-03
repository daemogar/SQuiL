namespace SQuiL;

using System;

/// <summary>
/// Optional. Declares which <see cref="SQuiLDialect"/> a data-context class targets.
/// When present, the generator uses the declared dialect. When absent, the generator infers the
/// dialect from the single referenced provider package (e.g. <c>SQuiL.SqlServer</c> or
/// <c>SQuiL.Sqlite</c>); if no provider is referenced it defaults to SQL Server, and if 2+
/// providers are referenced it is ambiguous — apply this attribute to disambiguate (SP0039).
/// </summary>
/// <param name="Dialect">The dialect this data context targets.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SQuiLDialectAttribute(SQuiLDialect Dialect) : Attribute
{
	/// <summary>The dialect this data context targets.</summary>
	public SQuiLDialect Dialect { get; } = Dialect;
}
