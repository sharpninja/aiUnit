using SharpNinja.AiUnit.Repl;

return await AiUnitReplCommandLine.ExecuteAsync(
	args,
	Console.In,
	Console.Out,
	Console.Error,
	emitPrompt: !Console.IsInputRedirected);
