using System.Runtime.CompilerServices;

namespace SquilParser.Tests;

public static class ModuleInitializer
{
	[ModuleInitializer]
	public static void Init()
	{
		VerifyDiffPlex.Initialize();
		VerifySourceGenerators.Initialize();
	}
}