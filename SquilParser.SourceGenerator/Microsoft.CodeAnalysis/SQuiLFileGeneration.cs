using SQuiL.Models;

namespace Microsoft.CodeAnalysis;

public class SQuiLFileGeneration(string Method, string Scope)
{
	public string Method { get; } = Method;
	public string Scope { get; } = Scope;
	public SQuiLDataContext Context { get; set; } = default!;
	public SQuiLModel Request { get; set; } = default!;
	public SQuiLModel Response { get; set; } = default!;
	public List<SQuiLTable> Tables { get; } = [];
}
