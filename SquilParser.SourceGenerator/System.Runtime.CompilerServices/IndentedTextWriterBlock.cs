namespace System.CodeDom.Compiler;

public class IndentedTextWriterBlock(string? Text, Action? Callback)
{
	private readonly string? Text = Text;
	public bool IsText => Text is not null;
	public static implicit operator string(IndentedTextWriterBlock block)
		=> (block.IsText ? block.Text : null) ?? throw new Exception("Object is not string text.");

	private readonly Action? Callback = Callback;
	public bool IsCallback => Callback is not null;
	public static implicit operator Action(IndentedTextWriterBlock block)
		=> (block.IsCallback ? block.Callback : null) ?? throw new Exception("Object is not a callback.");

	public static implicit operator IndentedTextWriterBlock(string text) => new(text, default!);
	public static implicit operator IndentedTextWriterBlock(Action callback) => new(default!, callback);
}
