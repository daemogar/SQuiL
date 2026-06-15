using SQuiL.Tools.GuideRenderer;

string? input = null, output = null, env = null;
for (var i = 0; i < args.Length; i++)
	switch (args[i])
	{
		case "--in" when i + 1 < args.Length: input = args[++i]; break;
		case "--out" when i + 1 < args.Length: output = args[++i]; break;
		case "--env" when i + 1 < args.Length: env = args[++i]; break;
		default:
			Console.Error.WriteLine($"Unexpected or incomplete argument: {args[i]}");
			return 2;
	}

if (input is null || output is null || env is null)
{
	Console.Error.WriteLine("Usage: GuideRenderer --in <template> --out <output> --env <vscode|ssms|visualstudio>");
	return 2;
}

try
{
	File.WriteAllText(output, GuideTemplate.Render(File.ReadAllText(input), env));
	return 0;
}
catch (GuideTemplateException e)
{
	Console.Error.WriteLine($"GuideRenderer: {e.Message}");
	return 1;
}
