namespace SQuiL;

using System;

/// <summary>
/// Optional. Declares which <see cref="SQuiLDialect"/> a data-context class targets.
/// When present, the generator uses the declared dialect; when absent, it defaults to SQL Server.
/// (Phase 3A ships only the SqlServer provider — inferring a dialect from the single referenced
/// provider package is deferred to a later multi-provider phase.)
/// </summary>
/// <param name="Dialect">The dialect this data context targets.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SQuiLDialectAttribute(SQuiLDialect Dialect) : Attribute
{
	/// <summary>The dialect this data context targets.</summary>
	public SQuiLDialect Dialect { get; } = Dialect;
}
