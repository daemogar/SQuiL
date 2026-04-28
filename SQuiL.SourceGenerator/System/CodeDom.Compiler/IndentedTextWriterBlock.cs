namespace System.CodeDom.Compiler;

/// <summary>
/// A discriminated union passed to <see cref="IndentedTextWriterExtensions.Block"/> to represent
/// either a string header fragment or an <see cref="Action"/> callback that writes a braced block.
/// Implicit conversions allow callers to pass either type without explicit wrapping.
/// </summary>
/// <param name="Text">The string header text, or <c>null</c> when this instance wraps a callback.</param>
/// <param name="Callback">The action callback, or <c>null</c> when this instance wraps a string.</param>
public class IndentedTextWriterBlock(string? Text, Action? Callback)
{
	private readonly string? Text = Text;

	/// <summary><c>true</c> when this instance wraps a string header.</summary>
	public bool IsText => Text is not null;

	/// <summary>Unwraps the string value; throws if this is a callback instance.</summary>
	public static implicit operator string(IndentedTextWriterBlock block)
		=> (block.IsText ? block.Text : null) ?? throw new Exception("Object is not string text.");

	private readonly Action? Callback = Callback;

	/// <summary><c>true</c> when this instance wraps a callback.</summary>
	public bool IsCallback => Callback is not null;

	/// <summary>Unwraps the callback; throws if this is a string instance.</summary>
	public static implicit operator Action(IndentedTextWriterBlock block)
		=> (block.IsCallback ? block.Callback : null) ?? throw new Exception("Object is not a callback.");

	/// <summary>Wraps a string as a text block.</summary>
	public static implicit operator IndentedTextWriterBlock(string text) => new(text, default!);
	/// <summary>Wraps a callback as an action block.</summary>
	public static implicit operator IndentedTextWriterBlock(Action callback) => new(default!, callback);
}
