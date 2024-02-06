namespace System.CodeDom.Compiler;

public static class IndentedTextWriterExtensions
{
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
