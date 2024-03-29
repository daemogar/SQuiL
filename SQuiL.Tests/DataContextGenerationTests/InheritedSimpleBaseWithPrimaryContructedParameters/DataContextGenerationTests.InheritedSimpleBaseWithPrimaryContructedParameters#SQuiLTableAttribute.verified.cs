﻿//HintName: SQuiLTableAttribute.cs
// <auto-generated />

#nullable enable

namespace SQuiL;

[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
public class SQuiLTableAttribute : System.Attribute
{
	public TableType Type { get; }

	public SQuiLTableAttribute(TableType type)
	{
		Type = type;
	}
}