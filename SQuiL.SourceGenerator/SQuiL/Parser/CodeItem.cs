﻿using SQuiL.Tokenizer;

namespace SQuiL.SourceGenerator.Parser;

public record CodeItem(Token Identifier, Token Type)
{
	public string UniqueIdentifier() => $"{Type.Type} {Identifier.Value}";

	public bool IsNullable { get; init; }

	public string DataReader() => Type.DataReader();

	public string CSharpType(Func<string>? callback = default) => Type.CSharpType(callback) + (IsNullable ? "?" : "");

	public static Func<CodeItem, int, string> SqlProperty(string classname, string model)
		=> (p, i) => p.Type.SqlProperty(classname, model, i, p.Identifier.Value, $"item.{p.Identifier.Value}");
}
