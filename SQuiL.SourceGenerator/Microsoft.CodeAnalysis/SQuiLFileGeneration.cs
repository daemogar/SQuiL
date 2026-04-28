using SQuiL.Models;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// Aggregates everything the generator needs to emit all source files for a single
/// SQL query file / data-context class pair: the parsed context, request model,
/// response model, and any table types referenced by the query.
/// </summary>
/// <param name="Method">The SQL file name (without extension) used as the method and type name base.</param>
/// <param name="Scope">Reserved scope identifier (currently unused; kept for future use).</param>
public class SQuiLFileGeneration(string Method)
{
	/// <summary>The SQL file name (without extension) used as the method and type name base.</summary>
	public string Method { get; } = Method;

	/// <summary>Full path to the originating <c>.sql</c> additional file.</summary>
	public string FilePath { get; set; } = default!;

	/// <summary>The generated data-context class model for executing the query.</summary>
	public SQuiLDataContext Context { get; set; } = default!;

	/// <summary>The generated request model built from <c>@Param_*</c> declarations.</summary>
	public SQuiLModel Request { get; set; } = default!;

	/// <summary>The generated response model built from <c>@Return_*</c> declarations.</summary>
	public SQuiLModel Response { get; set; } = default!;

	/// <summary>All table types (<c>@Params_*</c> / <c>@Returns_*</c>) referenced by this query.</summary>
	public List<SQuiLTable> Tables { get; } = [];
}
