﻿//HintName: SQuiLError.g.cs
// <auto-generated />

#nullable enable

namespace SQuiL;

public partial record SQuiLError(
	int Number,
	int Severity,
	int State,
	int Line,
	string Procedure,
	string Message);
