using System.Runtime.CompilerServices;

namespace SQuiL.Tests;

public static class ModuleInitializer
{
	[ModuleInitializer]
	public static void Init()
	{
		VerifyDiffPlex.Initialize();
		VerifySourceGenerators.Initialize();
	}
}