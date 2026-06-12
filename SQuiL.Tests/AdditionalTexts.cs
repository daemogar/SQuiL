using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SQuiL.Tests;

/// <summary>An inline query string beginning with a "--Name: X" line.</summary>
internal sealed class AdditionalQuery(string Text) : AdditionalText
{
	public override string Path { get; } = $"{Text[0..Text.IndexOf('\n')].Split(':', 2)[1].Trim()}.sql";

	public override SourceText? GetText(CancellationToken cancellationToken = default)
		=> SourceText.From(Text[Text.IndexOf('\n')..].TrimStart());
}

/// <summary>A repo-relative path to a real .sql/.squil file on disk.</summary>
internal sealed class AdditionalFile(string Path) : AdditionalText
{
	public override string Path { get; } = Path;

	public override SourceText? GetText(CancellationToken cancellationToken = default)
	{
		var relative = Path
			.Replace('\\', System.IO.Path.DirectorySeparatorChar)
			.Replace('/', System.IO.Path.DirectorySeparatorChar);
		var text = File.ReadAllText(System.IO.Path.Combine("..", "..", "..", relative));
		return SourceText.From(text);
	}
}
