using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SharpNinja.AiUnit.Repl;

public enum AiUnitReplMode
{
	Repl,
	Tui,
	Scan,
	List,
	Show,
	Validate,
	Catalog,
	Apply,
	ApplyGlobal,
	Restore,
}

public sealed record AiUnitReplOptions(
	AiUnitReplMode Mode,
	bool ShowHelp,
	bool ShowVersion,
	string? WorkspacePath,
	string? Target,
	string? ScreenId,
	string? StrategyName,
	string? SourceProjectName,
	string? SnapshotPath,
	bool DryRun,
	bool Force);

public sealed record AiUnitReplParseResult(
	AiUnitReplOptions? Options,
	int ExitCode,
	string? Error)
{
	public bool Success => Options is not null && Error is null && ExitCode == 0;
}

public static class AiUnitReplCommandLine
{
	public const string ToolName = "aiunit";
	public static string ToolVersion { get; } = ResolveToolVersion();

	public static AiUnitReplParseResult Parse(IReadOnlyList<string> args)
	{
		ArgumentNullException.ThrowIfNull(args);

		var mode = AiUnitReplMode.Repl;
		var modeSet = false;
		string? workspacePath = null;
		string? target = null;
		string? screenId = null;
		string? strategyName = null;
		string? sourceProjectName = null;
		string? snapshotPath = null;
		var dryRun = false;
		var force = false;

		for (var i = 0; i < args.Count; i++)
		{
			var arg = args[i];

			if (string.IsNullOrWhiteSpace(arg))
			{
				continue;
			}

			switch (arg)
			{
				case "-h":
				case "--help":
				case "help":
					return Success(Options(mode, showHelp: true));
				case "--version":
				case "version":
					return Success(Options(mode, showVersion: true));
				case "--workspace":
					if (!TryReadValue(args, ref i, "--workspace", out workspacePath, out var workspaceError))
					{
						return Error(workspaceError);
					}

					break;
				case "--from":
					if (!TryReadValue(args, ref i, "--from", out sourceProjectName, out var fromError))
					{
						return Error(fromError);
					}

					break;
				case "--project":
				case "--target":
					if (!TryReadValue(args, ref i, arg, out target, out var targetError))
					{
						return Error(targetError);
					}

					break;
				case "--snapshot":
					if (!TryReadValue(args, ref i, "--snapshot", out snapshotPath, out var snapshotError))
					{
						return Error(snapshotError);
					}

					break;
				case "--screen":
					if (!TryReadValue(args, ref i, "--screen", out screenId, out var screenError))
					{
						return Error(screenError);
					}

					break;
				case "--dry-run":
					dryRun = true;
					break;
				case "--force":
					force = true;
					break;
				case "--mode":
					if (!TryReadValue(args, ref i, "--mode", out var modeValue, out var modeError))
					{
						return Error(modeError);
					}

					if (!TryParseMode(modeValue, out mode))
					{
						return Error($"Unsupported aiUnit mode '{modeValue}'. Expected repl, tui, or scan.");
					}

					modeSet = true;
					break;
				default:
					if (arg.StartsWith("--workspace=", StringComparison.Ordinal))
					{
						workspacePath = arg["--workspace=".Length..];
						if (string.IsNullOrWhiteSpace(workspacePath))
						{
							return Error("--workspace requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--from=", StringComparison.Ordinal))
					{
						sourceProjectName = arg["--from=".Length..];
						if (string.IsNullOrWhiteSpace(sourceProjectName))
						{
							return Error("--from requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--project=", StringComparison.Ordinal))
					{
						target = arg["--project=".Length..];
						if (string.IsNullOrWhiteSpace(target))
						{
							return Error("--project requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--target=", StringComparison.Ordinal))
					{
						target = arg["--target=".Length..];
						if (string.IsNullOrWhiteSpace(target))
						{
							return Error("--target requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--snapshot=", StringComparison.Ordinal))
					{
						snapshotPath = arg["--snapshot=".Length..];
						if (string.IsNullOrWhiteSpace(snapshotPath))
						{
							return Error("--snapshot requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--screen=", StringComparison.Ordinal))
					{
						screenId = arg["--screen=".Length..];
						if (string.IsNullOrWhiteSpace(screenId))
						{
							return Error("--screen requires a non-empty value.");
						}

						break;
					}

					if (arg.StartsWith("--mode=", StringComparison.Ordinal))
					{
						var inlineMode = arg["--mode=".Length..];
						if (!TryParseMode(inlineMode, out mode))
						{
							return Error($"Unsupported aiUnit mode '{inlineMode}'. Expected repl, tui, or scan.");
						}

						modeSet = true;
						break;
					}

					if (modeSet && (mode == AiUnitReplMode.Show || mode == AiUnitReplMode.Restore) && target is null)
					{
						target = arg;
						break;
					}

					if (modeSet && mode == AiUnitReplMode.Tui && screenId is null)
					{
						screenId = arg;
						break;
					}

					if (TryParseMode(arg, out var commandMode))
					{
						if (modeSet)
						{
							return Error("Specify aiUnit mode only once.");
						}

						mode = commandMode;
						modeSet = true;
						break;
					}

					if (modeSet && (mode == AiUnitReplMode.Apply || mode == AiUnitReplMode.ApplyGlobal) && strategyName is null)
					{
						strategyName = arg;
						break;
					}

					return arg.StartsWith("-", StringComparison.Ordinal)
						? Error($"Unknown aiUnit option '{arg}'.")
						: Error($"Unknown aiUnit command '{arg}'.");
			}
		}

		if (mode == AiUnitReplMode.Show && string.IsNullOrWhiteSpace(target))
		{
			return Error("show requires a project name.");
		}

		if (mode == AiUnitReplMode.Restore && string.IsNullOrWhiteSpace(target))
		{
			return Error("restore requires a project name.");
		}

		if ((mode == AiUnitReplMode.Apply || mode == AiUnitReplMode.ApplyGlobal) && string.IsNullOrWhiteSpace(strategyName))
		{
			return Error($"{ModeName(mode)} requires a strategy name.");
		}

		if (mode == AiUnitReplMode.Apply && string.IsNullOrWhiteSpace(target))
		{
			return Error("apply requires --project <name>.");
		}

		return Success(Options(mode));

		AiUnitReplOptions Options(
			AiUnitReplMode selectedMode,
			bool showHelp = false,
			bool showVersion = false) =>
			new(
				selectedMode,
				showHelp,
				showVersion,
				workspacePath,
				target,
				screenId,
				strategyName,
				sourceProjectName,
				snapshotPath,
				dryRun,
				force);
	}

	public static Task<int> ExecuteAsync(
		IReadOnlyList<string> args,
		TextWriter stdout,
		TextWriter stderr)
	{
		return ExecuteAsync(
			args,
			TextReader.Null,
			stdout,
			stderr,
			emitPrompt: false);
	}

	public static Task<int> ExecuteAsync(
		IReadOnlyList<string> args,
		TextReader stdin,
		TextWriter stdout,
		TextWriter stderr)
	{
		return ExecuteAsync(
			args,
			stdin,
			stdout,
			stderr,
			emitPrompt: false);
	}

	public static Task<int> ExecuteAsync(
		IReadOnlyList<string> args,
		TextReader stdin,
		TextWriter stdout,
		TextWriter stderr,
		bool emitPrompt)
	{
		ArgumentNullException.ThrowIfNull(stdin);
		ArgumentNullException.ThrowIfNull(stdout);
		ArgumentNullException.ThrowIfNull(stderr);

		var parsed = Parse(args);
		if (!parsed.Success)
		{
			stderr.WriteLine(parsed.Error);
			stderr.WriteLine();
			WriteHelp(stderr);
			return Task.FromResult(parsed.ExitCode);
		}

		var options = parsed.Options!;
		if (options.ShowHelp)
		{
			WriteHelp(stdout);
			return Task.FromResult(0);
		}

		if (options.ShowVersion)
		{
			stdout.WriteLine(ToolVersion);
			return Task.FromResult(0);
		}

		return Task.FromResult(ExecuteSelectedMode(options, stdin, stdout, stderr, emitPrompt));
	}

	public static void WriteHelp(TextWriter writer)
	{
		ArgumentNullException.ThrowIfNull(writer);

		writer.WriteLine("aiunit - AI review strategy REPL");
		writer.WriteLine();
		writer.WriteLine("Usage:");
		writer.WriteLine("  aiunit [repl|tui|scan|list|catalog|validate] [--workspace <path>]");
		writer.WriteLine("  aiunit tui [overview|projects|catalog|validate] [--workspace <path>]");
		writer.WriteLine("  aiunit show <project> [--workspace <path>]");
		writer.WriteLine("  aiunit apply <strategy> --project <project> [--from <project>] [--dry-run] [--force]");
		writer.WriteLine("  aiunit apply-global <strategy> [--from <project>] [--dry-run] [--force]");
		writer.WriteLine("  aiunit restore <project> [--snapshot <path>]");
		writer.WriteLine("  aiunit --mode <repl|tui|scan|list|catalog|validate> [--workspace <path>]");
		writer.WriteLine("  aiunit --help");
		writer.WriteLine("  aiunit --version");
		writer.WriteLine();
		writer.WriteLine("Modes:");
		writer.WriteLine("  repl   Start the command-driven REPL shell (default).");
		writer.WriteLine("  tui    Start the terminal UI shell.");
		writer.WriteLine("  scan   Run one-shot workspace discovery.");
		writer.WriteLine("  list   List discovered aiUnit-enabled projects.");
		writer.WriteLine("  show   Show read-only details for one discovered project.");
		writer.WriteLine("  catalog   List reusable strategies discovered across projects.");
		writer.WriteLine("  apply   Apply one catalog strategy to a selected project.");
		writer.WriteLine("  apply-global   Apply one catalog strategy to every discovered project.");
		writer.WriteLine("  restore   Restore a project config from the latest or selected snapshot.");
		writer.WriteLine("  validate   Validate discovered project configuration.");
	}

	private static int ExecuteSelectedMode(
		AiUnitReplOptions options,
		TextReader stdin,
		TextWriter stdout,
		TextWriter stderr,
		bool emitPrompt)
	{
		switch (options.Mode)
		{
			case AiUnitReplMode.Repl:
				return RunRepl(options, stdin, stdout, stderr, emitPrompt);
			case AiUnitReplMode.Scan:
				WriteScan(options, stdout);
				return 0;
			case AiUnitReplMode.List:
				WriteList(options, stdout);
				return 0;
			case AiUnitReplMode.Show:
				return WriteShow(options, stdout, stderr);
			case AiUnitReplMode.Tui:
				return WriteTui(options, stdout, stderr);
			case AiUnitReplMode.Validate:
				return WriteValidation(options, stdout);
			case AiUnitReplMode.Catalog:
				WriteCatalog(options, stdout);
				return 0;
			case AiUnitReplMode.Apply:
			case AiUnitReplMode.ApplyGlobal:
				return WriteApply(options, stdout);
			case AiUnitReplMode.Restore:
				return WriteRestore(options, stdout, stderr);
			default:
				return RunRepl(options, stdin, stdout, stderr, emitPrompt);
		}
	}

	private static int RunRepl(
		AiUnitReplOptions options,
		TextReader stdin,
		TextWriter stdout,
		TextWriter stderr,
		bool emitPrompt)
	{
		stdout.WriteLine("aiunit repl");
		stdout.WriteLine($"workspace: {WorkspaceDisplay(options.WorkspacePath)}");
		stdout.WriteLine("type 'help' for commands; 'exit' to quit.");

		var exitCode = 0;
		while (true)
		{
			if (emitPrompt)
			{
				stdout.Write("aiunit> ");
				stdout.Flush();
			}

			var line = stdin.ReadLine();
			if (line is null)
			{
				if (emitPrompt)
				{
					stdout.WriteLine();
				}

				return exitCode;
			}

			line = line.Trim();
			if (line.Length == 0)
			{
				continue;
			}

			if (IsExitCommand(line))
			{
				stdout.WriteLine("bye");
				return exitCode;
			}

			if (IsHelpCommand(line))
			{
				WriteReplHelp(stdout);
				continue;
			}

			var commandArgs = SplitCommandLine(line, out var splitError);
			if (splitError is not null)
			{
				stderr.WriteLine(splitError);
				exitCode = 2;
				continue;
			}

			if (commandArgs.Count == 0)
			{
				continue;
			}

			if (string.Equals(commandArgs[0], "repl", StringComparison.OrdinalIgnoreCase))
			{
				stderr.WriteLine("The REPL is already running. Enter a command or 'exit'.");
				exitCode = 2;
				continue;
			}

			var parsed = Parse(WithWorkspace(commandArgs, options.WorkspacePath));
			if (!parsed.Success)
			{
				stderr.WriteLine(parsed.Error);
				stderr.WriteLine("type 'help' for commands; 'exit' to quit.");
				exitCode = parsed.ExitCode;
				continue;
			}

			if (parsed.Options!.ShowHelp)
			{
				WriteReplHelp(stdout);
				continue;
			}

			if (parsed.Options.ShowVersion)
			{
				stdout.WriteLine(ToolVersion);
				continue;
			}

			if (parsed.Options.Mode == AiUnitReplMode.Repl)
			{
				stderr.WriteLine("Enter a command such as scan, list, show, catalog, validate, apply, restore, or tui.");
				exitCode = 2;
				continue;
			}

			var commandExitCode = ExecuteSelectedMode(
				parsed.Options,
				TextReader.Null,
				stdout,
				stderr,
				emitPrompt: false);
			if (commandExitCode != 0)
			{
				exitCode = commandExitCode;
			}
		}
	}

	private static void WriteReplHelp(TextWriter writer)
	{
		writer.WriteLine("Commands:");
		writer.WriteLine("  scan | list | catalog | validate");
		writer.WriteLine("  show <project>");
		writer.WriteLine("  apply <strategy> --project <project> [--from <project>] [--dry-run] [--force]");
		writer.WriteLine("  apply-global <strategy> [--from <project>] [--dry-run] [--force]");
		writer.WriteLine("  restore <project> [--snapshot <path>]");
		writer.WriteLine("  tui [overview|projects|catalog|validate]");
		writer.WriteLine("  help | version | exit");
	}

	private static bool IsExitCommand(string line) =>
		string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(line, "quit", StringComparison.OrdinalIgnoreCase);

	private static bool IsHelpCommand(string line) =>
		string.Equals(line, "help", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(line, "?", StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyList<string> SplitCommandLine(string line, out string? error)
	{
		var args = new List<string>();
		var current = new StringBuilder();
		var inQuotes = false;

		for (var i = 0; i < line.Length; i++)
		{
			var character = line[i];
			if (character == '\\' && inQuotes && i + 1 < line.Length && line[i + 1] == '"')
			{
				current.Append('"');
				i++;
				continue;
			}

			if (character == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if (char.IsWhiteSpace(character) && !inQuotes)
			{
				AddCurrent();
				continue;
			}

			current.Append(character);
		}

		if (inQuotes)
		{
			error = "Unterminated quoted argument.";
			return Array.Empty<string>();
		}

		AddCurrent();
		error = null;
		return args;

		void AddCurrent()
		{
			if (current.Length == 0)
			{
				return;
			}

			args.Add(current.ToString());
			current.Clear();
		}
	}

	private static IReadOnlyList<string> WithWorkspace(
		IReadOnlyList<string> args,
		string? workspacePath)
	{
		if (string.IsNullOrWhiteSpace(workspacePath)
			|| args.Any(arg =>
				string.Equals(arg, "--workspace", StringComparison.Ordinal)
				|| arg.StartsWith("--workspace=", StringComparison.Ordinal)))
		{
			return args;
		}

		var merged = new List<string>(args.Count + 2);
		merged.AddRange(args);
		merged.Add("--workspace");
		merged.Add(workspacePath);
		return merged;
	}

	private static int WriteTui(
		AiUnitReplOptions options,
		TextWriter stdout,
		TextWriter stderr)
	{
		try
		{
			stdout.Write(AiUnitTuiRenderer.RenderToText(
				WorkspaceDisplay(options.WorkspacePath),
				options.ScreenId));
			return 0;
		}
		catch (ArgumentException ex)
		{
			stderr.WriteLine(ex.Message);
			return 1;
		}
	}

	private static void WriteScan(AiUnitReplOptions options, TextWriter stdout)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(WorkspaceDisplay(options.WorkspacePath));
		stdout.WriteLine($"aiUnit scan root: {discovery.RootPath}");
		stdout.WriteLine($"projects: {discovery.Projects.Count}");
		if (discovery.Projects.Count == 0)
		{
			stdout.WriteLine("no aiUnit-enabled projects found");
			return;
		}

		foreach (var project in discovery.Projects)
		{
			stdout.WriteLine(ProjectSummary(project));
		}
	}

	private static void WriteList(AiUnitReplOptions options, TextWriter stdout)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(WorkspaceDisplay(options.WorkspacePath));
		if (discovery.Projects.Count == 0)
		{
			stdout.WriteLine("no aiUnit-enabled projects found");
			return;
		}

		foreach (var project in discovery.Projects)
		{
			stdout.WriteLine($"{project.ProjectName} | active: {ActiveDisplay(project)} | status: {StatusDisplay(project.Status)}");
		}
	}

	private static int WriteShow(
		AiUnitReplOptions options,
		TextWriter stdout,
		TextWriter stderr)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(WorkspaceDisplay(options.WorkspacePath));
		var project = discovery.FindProject(options.Target!);
		if (project is null)
		{
			stderr.WriteLine($"Project '{options.Target}' was not found.");
			return 1;
		}

		stdout.WriteLine(project.ProjectName);
		stdout.WriteLine($"  project: {project.ProjectPath}");
		stdout.WriteLine($"  config: {project.ConfigPath ?? "(missing)"}");
		stdout.WriteLine($"  refs: {project.ReferenceSummary}");
		stdout.WriteLine($"  active: {ActiveDisplay(project)}");
		stdout.WriteLine($"  strategies: {project.StrategyCount}");
		foreach (var strategy in project.StrategyNames)
		{
			stdout.WriteLine($"    - {strategy}");
		}

		foreach (var message in project.Messages)
		{
			stdout.WriteLine($"  {StatusDisplay(project.Status)}: {message}");
		}

		return project.Status == AiUnitDiscoveryStatus.Error ? 1 : 0;
	}

	private static int WriteValidation(AiUnitReplOptions options, TextWriter stdout)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(WorkspaceDisplay(options.WorkspacePath));
		if (discovery.Projects.Count == 0)
		{
			stdout.WriteLine("no aiUnit-enabled projects found");
			return 1;
		}

		foreach (var project in discovery.Projects)
		{
			stdout.WriteLine(ProjectSummary(project));
			foreach (var message in project.Messages)
			{
				stdout.WriteLine($"  - {message}");
			}
		}

		return discovery.HasErrors ? 1 : 0;
	}

	private static void WriteCatalog(AiUnitReplOptions options, TextWriter stdout)
	{
		var entries = new AiUnitStrategyCatalogService().Catalog(WorkspaceDisplay(options.WorkspacePath));
		if (entries.Count == 0)
		{
			stdout.WriteLine("no reusable strategies found");
			return;
		}

		foreach (var entry in entries)
		{
			var active = entry.IsActive ? "active" : "available";
			stdout.WriteLine($"{entry.StrategyName} | {entry.ProjectName} | {entry.Definition.Kind} | {active} | {entry.ConfigPath}");
		}
	}

	private static int WriteApply(AiUnitReplOptions options, TextWriter stdout)
	{
		var service = new AiUnitStrategyCatalogService();
		var targetProjects = options.Mode == AiUnitReplMode.ApplyGlobal
			? null
			: new[] { options.Target! };
		var result = service.Apply(
			WorkspaceDisplay(options.WorkspacePath),
			options.StrategyName!,
			options.SourceProjectName,
			targetProjects,
			options.DryRun,
			options.Force);

		stdout.WriteLine(options.DryRun ? "dry-run: true" : "dry-run: false");
		foreach (var project in result.Projects)
		{
			var status = project.Conflict ? "conflict" : project.Changed ? "changed" : "unchanged";
			stdout.WriteLine($"[{status}] {project.ProjectName}: {project.Message}");
			if (!string.IsNullOrWhiteSpace(project.SnapshotPath))
			{
				stdout.WriteLine($"  snapshot: {project.SnapshotPath}");
			}
		}

		return result.HasConflicts ? 1 : 0;
	}

	private static int WriteRestore(
		AiUnitReplOptions options,
		TextWriter stdout,
		TextWriter stderr)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(WorkspaceDisplay(options.WorkspacePath));
		var project = discovery.FindProject(options.Target!);
		if (project is null)
		{
			stderr.WriteLine($"Project '{options.Target}' was not found.");
			return 1;
		}

		if (project.ConfigPath is null)
		{
			stderr.WriteLine($"Project '{options.Target}' has no config file to restore.");
			return 1;
		}

		var service = new AiUnitConfigSnapshotService();
		var result = string.IsNullOrWhiteSpace(options.SnapshotPath)
			? service.RestoreLatest(project.ConfigPath)
			: service.RestoreSnapshot(project.ConfigPath, options.SnapshotPath!);
		if (!result.Success)
		{
			stderr.WriteLine(result.Error);
			return 1;
		}

		stdout.WriteLine($"restored {project.ProjectName}");
		stdout.WriteLine($"snapshot: {result.SnapshotPath}");
		return 0;
	}

	private static bool TryReadValue(
		IReadOnlyList<string> args,
		ref int index,
		string option,
		out string? value,
		out string error)
	{
		if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
		{
			value = null;
			error = $"{option} requires a value.";
			return false;
		}

		value = args[++index];
		error = string.Empty;
		return true;
	}

	private static bool TryParseMode(string? value, out AiUnitReplMode mode)
	{
		mode = AiUnitReplMode.Repl;

		if (string.Equals(value, "repl", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Repl;
			return true;
		}

		if (string.Equals(value, "tui", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Tui;
			return true;
		}

		if (string.Equals(value, "scan", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Scan;
			return true;
		}

		if (string.Equals(value, "list", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.List;
			return true;
		}

		if (string.Equals(value, "show", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Show;
			return true;
		}

		if (string.Equals(value, "validate", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Validate;
			return true;
		}

		if (string.Equals(value, "catalog", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Catalog;
			return true;
		}

		if (string.Equals(value, "apply", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Apply;
			return true;
		}

		if (string.Equals(value, "apply-global", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "applyGlobal", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.ApplyGlobal;
			return true;
		}

		if (string.Equals(value, "restore", StringComparison.OrdinalIgnoreCase))
		{
			mode = AiUnitReplMode.Restore;
			return true;
		}

		return false;
	}

	private static string ModeName(AiUnitReplMode mode) =>
		mode switch
		{
			AiUnitReplMode.Repl => "repl",
			AiUnitReplMode.Tui => "tui",
			AiUnitReplMode.Scan => "scan",
			AiUnitReplMode.List => "list",
			AiUnitReplMode.Show => "show",
			AiUnitReplMode.Validate => "validate",
			AiUnitReplMode.Catalog => "catalog",
			AiUnitReplMode.Apply => "apply",
			AiUnitReplMode.ApplyGlobal => "apply-global",
			AiUnitReplMode.Restore => "restore",
			_ => mode.ToString(),
		};

	private static string WorkspaceDisplay(string? workspacePath) =>
		string.IsNullOrWhiteSpace(workspacePath)
			? Directory.GetCurrentDirectory()
			: workspacePath;

	private static string ProjectSummary(AiUnitDiscoveredProject project) =>
		$"[{StatusDisplay(project.Status)}] {project.ProjectName} | active: {ActiveDisplay(project)} | strategies: {project.StrategyCount} | refs: {project.ReferenceSummary} | config: {project.ConfigDisplay}";

	private static string ActiveDisplay(AiUnitDiscoveredProject project) =>
		string.IsNullOrWhiteSpace(project.ActiveStrategy) ? "(none)" : project.ActiveStrategy!;

	private static string StatusDisplay(AiUnitDiscoveryStatus status) =>
		status switch
		{
			AiUnitDiscoveryStatus.Ok => "ok",
			AiUnitDiscoveryStatus.Warning => "warning",
			AiUnitDiscoveryStatus.Error => "error",
			_ => status.ToString().ToLowerInvariant(),
		};

	private static string ResolveToolVersion()
	{
		var version =
			typeof(AiUnitReplCommandLine).Assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
				?.InformationalVersion
			?? typeof(AiUnitReplCommandLine).Assembly.GetName().Version?.ToString()
			?? "0.0.0";

		var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
		return metadataIndex > 0 ? version[..metadataIndex] : version;
	}

	private static AiUnitReplParseResult Success(AiUnitReplOptions options) =>
		new(options, ExitCode: 0, Error: null);

	private static AiUnitReplParseResult Error(string error) =>
		new(Options: null, ExitCode: 2, Error: error);
}
