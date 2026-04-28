namespace System.CodeDom.Compiler;

/// <summary>Extension methods on <see cref="IndentedTextWriter"/> for emitting indented code blocks.</summary>
public static class IndentedTextWriterExtensions
{
	/// <summary>
	/// Writes <paramref name="header"/> line(s) respecting leading-tab indentation, then invokes
	/// <paramref name="callback"/> (if provided) surrounded by <c>{</c> / <c>}</c>, and repeats
	/// the same pattern for any additional <paramref name="blocks"/>.
	/// </summary>
	public static void Block(
		this IndentedTextWriter writer,
		string header,
		Action callback = default!,
		params IndentedTextWriterBlock[] blocks)
	{
		Header(header);
		Invoke(callback);

		foreach (var block in blocks)
		{
			if (block.IsText) Header(block);
			if (block.IsCallback) Invoke(block);
		}

		void Header(string text)
		{
			if (text is null) return;

			foreach (var header in text.Replace(writer.NewLine, "\n").Split('\n'))
			{
				var tabs = header.TakeWhile(p => p == '\t').Count();
				writer.Indent += tabs;
				writer.WriteLine(header.TrimStart());
				writer.Indent -= tabs;
			}
		}

		void Invoke(Action callback)
		{
			if(callback is null) return;

			writer.WriteLine("{");
			writer.Indent++;
			callback();
			writer.Indent--;
			writer.WriteLine("}");
		}
	}
}
