﻿//HintName: MergedData.g.cs
// <auto-generated />

#nullable enable

namespace TestCase;

public partial record MergedData
{
	public int Number { get; init; }
	
	public string Message { get; init; }
	
	public MergedData(
		int number,
		string message): this()
		{
			Number = number;
			Message = message;
		}
	}
