namespace SQuiL.Tools.GuideRenderer;

/// <summary>Thrown when a guide template is malformed (unbalanced/nested markers or an unknown environment token).</summary>
public sealed class GuideTemplateException(string message) : Exception(message);

/// <summary>
/// Renders the canonical guide template down to a single environment by including only the
/// unmarked content plus the <c>&lt;!--#if env--&gt;</c> blocks whose token list contains the target.
/// Pure and filesystem-free so it can be unit-tested directly.
/// </summary>
public static class GuideTemplate
{
	private static readonly string[] KnownEnvironments = ["vscode", "ssms", "visualstudio"];

	/// <summary>Renders <paramref name="template"/> for <paramref name="env"/>.</summary>
	/// <exception cref="GuideTemplateException">The env is unknown or the template markers are malformed.</exception>
	public static string Render(string template, string env)
	{
		if (Array.IndexOf(KnownEnvironments, env) < 0)
			throw new GuideTemplateException(
				$"Unknown render environment '{env}'. Expected one of: {string.Join(", ", KnownEnvironments)}.");

		var lines = template.Split('\n');
		var emitted = new List<string>();
		string[]? currentTokens = null; // non-null => inside an #if block
		var openLine = 0;

		for (var i = 0; i < lines.Length; i++)
		{
			var trimmed = lines[i].Trim();

			if (trimmed.StartsWith("<!--#if", StringComparison.Ordinal))
			{
				if (!trimmed.EndsWith("-->", StringComparison.Ordinal))
					throw new GuideTemplateException($"Malformed <!--#if--> marker at line {i + 1}: must be on its own line and end with -->.");
				if (currentTokens is not null)
					throw new GuideTemplateException($"Nested <!--#if--> at line {i + 1}; the block opened at line {openLine} is still open. Nesting is not supported.");

				var inner = trimmed["<!--#if".Length..^"-->".Length].Trim();
				var tokens = inner.Split([' '], StringSplitOptions.RemoveEmptyEntries);
				if (tokens.Length == 0)
					throw new GuideTemplateException($"Empty <!--#if--> token list at line {i + 1}.");
				foreach (var token in tokens)
					if (Array.IndexOf(KnownEnvironments, token) < 0)
						throw new GuideTemplateException(
							$"Unknown environment token '{token}' in <!--#if--> at line {i + 1}. Expected one of: {string.Join(", ", KnownEnvironments)}.");

				currentTokens = tokens;
				openLine = i + 1;
				continue; // drop the marker line
			}

			if (trimmed.StartsWith("<!--#endif", StringComparison.Ordinal))
			{
				if (trimmed != "<!--#endif-->")
					throw new GuideTemplateException($"Malformed <!--#endif--> marker at line {i + 1}: must be exactly <!--#endif--> on its own line.");
				if (currentTokens is null)
					throw new GuideTemplateException($"Stray <!--#endif--> at line {i + 1} with no matching <!--#if-->.");
				currentTokens = null;
				continue; // drop the marker line
			}

			if (currentTokens is null || Array.IndexOf(currentTokens, env) >= 0)
				emitted.Add(lines[i]);
		}

		if (currentTokens is not null)
			throw new GuideTemplateException($"Unbalanced <!--#if--> opened at line {openLine}; missing <!--#endif-->.");

		return string.Join("\n", emitted);
	}
}
