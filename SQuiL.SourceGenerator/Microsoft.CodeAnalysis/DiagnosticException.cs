using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

/// <summary>
/// An exception that carries Roslyn source location information so the tokenizer and parser
/// can surface precise error positions as <see cref="Diagnostic"/> entries instead of
/// crashing the generator.  Caught at the generator boundary and converted via
/// <see cref="DiagnosticsMessages.ReportLexicalParseErrorDiagnostic"/>.
/// </summary>
public class DiagnosticException : Exception
{
	private TextSpan TextSpan { get; }

	private LinePositionSpan LinePositionSpan { get; }

	private Location? Location { get; set; }

	/// <summary>
	/// Resolves the Roslyn <see cref="Microsoft.CodeAnalysis.Location"/> for the error,
	/// constructing it from the stored span data on the first call and caching the result.
	/// </summary>
	/// <param name="filename">The source file path to associate with the location.</param>
	public Location GetLocation(string filename)
		=> Location ??= Location.Create(filename, TextSpan, LinePositionSpan);

	/// <summary>
	/// Creates an exception with no source position (e.g. a structural/semantic error
	/// not tied to a specific character range).
	/// </summary>
	/// <param name="message">Human-readable error description.</param>
	public DiagnosticException(string message) : base(message)
	{
		Location = Location.None;
	}

	/// <summary>
	/// Creates an exception pointing to a single character at the given line/column.
	/// </summary>
	/// <param name="start">Absolute character offset in the source text.</param>
	/// <param name="line">Zero-based line number.</param>
	/// <param name="column">Zero-based column number.</param>
	/// <param name="message">Human-readable error description.</param>
	public DiagnosticException(int start, int line, int column, string message)
		: base(message)
	{
		TextSpan = new(start, 1);
		LinePositionSpan = new(new(line, column), new(line, column + 1));
	}

	/// <summary>
	/// Creates an exception spanning a run of characters on a single line.
	/// </summary>
	/// <param name="start">Absolute character offset in the source text.</param>
	/// <param name="length">Number of characters in the highlighted span.</param>
	/// <param name="line">Zero-based line number.</param>
	/// <param name="column">Zero-based starting column.</param>
	/// <param name="message">Human-readable error description.</param>
	public DiagnosticException(int start, int length, int line, int column, string message)
		: base(message)
	{
		TextSpan = new(start, length);
		LinePositionSpan = new(new(line, column), new(line, column + length));
	}

	/// <summary>
	/// Creates an exception spanning an arbitrary multi-line range.
	/// </summary>
	/// <param name="start">Absolute character offset in the source text.</param>
	/// <param name="length">Number of characters in the highlighted span.</param>
	/// <param name="beginLine">Zero-based line where the span starts.</param>
	/// <param name="beginColumn">Zero-based column where the span starts.</param>
	/// <param name="endLine">Zero-based line where the span ends.</param>
	/// <param name="endColumn">Zero-based column where the span ends.</param>
	/// <param name="message">Human-readable error description.</param>
	public DiagnosticException(int start, int length, int beginLine, int beginColumn, int endLine, int endColumn, string message)
		: base(message)
	{
		TextSpan = new(start, length);
		LinePositionSpan = new(new(beginLine, beginColumn), new(endLine, endColumn));
	}
}
