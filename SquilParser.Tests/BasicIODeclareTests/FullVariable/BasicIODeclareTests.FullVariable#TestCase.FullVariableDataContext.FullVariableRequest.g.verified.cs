﻿//HintName: TestCase.FullVariableDataContext.FullVariableRequest.g.cs
// <auto-generated />

#nullable enable

namespace TestCase;

public partial record FullVariableRequest
{
	public bool Debug { get; set; }
	
	public int? Scaler { get; set; }
	
	public FullVariableRequestObjectObject? Object { get; set; } = default!;
	
	public System.Collections.Generic.List<FullVariableRequestTableTable> Table { get; set; } = [];
}
