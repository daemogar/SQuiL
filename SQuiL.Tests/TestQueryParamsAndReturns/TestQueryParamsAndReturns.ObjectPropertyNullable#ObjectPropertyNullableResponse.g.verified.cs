﻿//HintName: ObjectPropertyNullableResponse.g.cs
// <auto-generated />

#nullable enable

namespace TestCase;

public partial record ObjectPropertyNullableResponse
{
	public StudentObject? Student { get; set; } = default!;
	
	public System.Collections.Generic.List<ParentsTable> Parents { get; set; } = [];
}
