namespace SQuiL;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// An <see cref="AggregateException"/> that bundles all SQL errors returned by a query
/// into a single throwable exception. Each inner exception wraps one <see cref="SQuiLError"/>
/// via <see cref="SQuiLError.AsException"/>.
/// </summary>
/// <param name="Errors">The full list of SQL errors reported by the query.</param>
public class SQuiLAggregateException(IReadOnlyList<SQuiLError> Errors)
	: AggregateException(Errors.Select(p => p.AsException()))
{ }
