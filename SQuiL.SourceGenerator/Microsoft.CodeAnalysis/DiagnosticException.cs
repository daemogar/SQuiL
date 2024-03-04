using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis;

public class DiagnosticException : Exception
{
	private TextSpan TextSpan { get; }

	private LinePositionSpan LinePositionSpan { get; }

	private Location? Location { get; set; }

	public Location GetLocation(string filename)
		=> Location ??= Location.Create(filename, TextSpan, LinePositionSpan);

	public DiagnosticException(string message) : base(message)
	{
		Location = Location.None;
	}

	public DiagnosticException(int start, int line, int column, string message)
		: base(message)
	{
		TextSpan = new(start, 1);
		LinePositionSpan = new(new(line, column), new(line, column + 1));
	}

	public DiagnosticException(int start, int length, int line, int column, string message)
		: base(message)
	{
		TextSpan = new(start, length);
		LinePositionSpan = new(new(line, column), new(line, column + length));
	}

	public DiagnosticException(int start, int length, int beginLine, int beginColumn, int endLine, int endColumn, string message)
		: base(message)
	{
		TextSpan = new(start, length);
		LinePositionSpan = new(new(beginLine, beginColumn), new(endLine, endColumn));
	}
}
