using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spectre.Console;

namespace SharpNinja.AiUnit.Repl;

public enum AiUnitTuiScreen
{
	Overview,
	Projects,
	Catalog,
	Validate,
}

public static class AiUnitTuiRenderer
{
	public static string RenderToText(
		string rootPath,
		string? screenId = null,
		int width = 120)
	{
		var screen = ParseScreen(screenId);
		var writer = new StringWriter();
		var console = AnsiConsole.Create(new AnsiConsoleSettings
		{
			Ansi = AnsiSupport.No,
			ColorSystem = ColorSystemSupport.NoColors,
			Out = new AnsiConsoleOutput(writer),
			Interactive = InteractionSupport.No,
		});
		console.Profile.Width = width;

		Render(rootPath, screen, console);
		return writer.ToString();
	}

	public static AiUnitTuiScreen ParseScreen(string? screenId)
	{
		if (string.IsNullOrWhiteSpace(screenId) || string.Equals(screenId, "overview", StringComparison.OrdinalIgnoreCase))
		{
			return AiUnitTuiScreen.Overview;
		}

		if (string.Equals(screenId, "project", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(screenId, "projects", StringComparison.OrdinalIgnoreCase))
		{
			return AiUnitTuiScreen.Projects;
		}

		if (string.Equals(screenId, "catalog", StringComparison.OrdinalIgnoreCase))
		{
			return AiUnitTuiScreen.Catalog;
		}

		if (string.Equals(screenId, "validate", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(screenId, "validation", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(screenId, "deploy", StringComparison.OrdinalIgnoreCase))
		{
			return AiUnitTuiScreen.Validate;
		}

		throw new ArgumentException($"Unknown TUI screen '{screenId}'.", nameof(screenId));
	}

	private static void Render(
		string rootPath,
		AiUnitTuiScreen screen,
		IAnsiConsole console)
	{
		var root = Path.GetFullPath(rootPath);
		var discovery = AiUnitWorkspaceDiscovery.Discover(root);
		var catalog = new AiUnitStrategyCatalogService().Catalog(root);

		WriteHeader(console, screen, root, discovery, catalog);
		WriteNavigation(console, screen);

		switch (screen)
		{
			case AiUnitTuiScreen.Overview:
				WriteOverview(console, discovery);
				break;
			case AiUnitTuiScreen.Projects:
				WriteProjectEditor(console, discovery);
				break;
			case AiUnitTuiScreen.Catalog:
				WriteCatalog(console, catalog, discovery);
				break;
			case AiUnitTuiScreen.Validate:
				WriteValidation(console, discovery);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(screen), screen, "Unknown TUI screen.");
		}
	}

	private static void WriteHeader(
		IAnsiConsole console,
		AiUnitTuiScreen screen,
		string root,
		AiUnitWorkspaceDiscoveryResult discovery,
		IReadOnlyList<AiUnitStrategyCatalogEntry> catalog)
	{
		console.Write(new Rule($"aiunit tui - {ScreenTitle(screen)}"));
		var warningCount = discovery.Projects.Count(project => project.Status != AiUnitDiscoveryStatus.Ok);
		console.WriteLine($"root: {root} | mode: TUI | projects: {discovery.Projects.Count} | strategies: {catalog.Count} | warnings: {warningCount}");
		console.WriteLine();
	}

	private static void WriteNavigation(IAnsiConsole console, AiUnitTuiScreen screen)
	{
		var table = new Table { Border = TableBorder.Ascii, ShowHeaders = false };
		table.AddColumn("Navigation");
		table.AddColumn("Commands");
		table.AddRow(Nav("Overview", screen == AiUnitTuiScreen.Overview), "scan | list | apply-global | restore");
		table.AddRow(Nav("Projects", screen == AiUnitTuiScreen.Projects), "set-active | add-strategy | restore snapshot");
		table.AddRow(Nav("Catalog", screen == AiUnitTuiScreen.Catalog), "catalog | apply | apply-global --dry-run");
		table.AddRow(Nav("Validate", screen == AiUnitTuiScreen.Validate), "validate | pack | screenshot gates");
		console.Write(table);
		console.WriteLine();
	}

	private static void WriteOverview(
		IAnsiConsole console,
		AiUnitWorkspaceDiscoveryResult discovery)
	{
		console.Write(new Rule("Discovered aiUnit Projects"));
		var table = new Table { Border = TableBorder.Ascii };
		table.AddColumn("Project");
		table.AddColumn("Active");
		table.AddColumn("Count");
		table.AddColumn("State");
		foreach (var project in discovery.Projects)
		{
			table.AddRow(
				Escape(project.ProjectName),
				Escape(string.IsNullOrWhiteSpace(project.ActiveStrategy) ? "none" : project.ActiveStrategy!),
				project.StrategyCount.ToString(),
				State(project));
		}

		if (discovery.Projects.Count == 0)
		{
			table.AddRow("(none)", "none", "0", "no projects");
		}

		console.Write(table);
		var configsPresent = discovery.Projects.Count(project => project.ConfigPath is not null);
		var configsMissing = discovery.Projects.Count(project => project.ConfigPath is null);
		console.WriteLine($"Scan Summary: configs: {configsPresent} present, {configsMissing} missing");
		console.WriteLine($"package refs: SharpNinja.aiUnit found in {discovery.Projects.Count(project => project.HasPackageReference)} projects");
		console.WriteLine("next action: configure missing project or apply global strategy");
	}

	private static void WriteProjectEditor(
		IAnsiConsole console,
		AiUnitWorkspaceDiscoveryResult discovery)
	{
		var project = discovery.Projects.FirstOrDefault(project => project.ConfigPath is not null)
			?? discovery.Projects.FirstOrDefault();
		console.Write(new Rule("Project Strategy Editor"));
		if (project is null)
		{
			console.WriteLine("No project selected.");
			return;
		}

		console.WriteLine($"project: {project.ProjectName} | config {State(project)}");
		var table = new Table { Border = TableBorder.Ascii };
		table.AddColumn("Name");
		table.AddColumn("Kind");
		table.AddColumn("Model/Command");
		table.AddColumn("State");

		if (project.ConfigPath is not null)
		{
			var editor = AiUnitStrategyConfigEditor.Load(project.ConfigPath);
			foreach (var strategyName in editor.StrategyNames)
			{
				editor.TryGetStrategyDefinition(strategyName, out var definition);
				table.AddRow(
					Escape(string.Equals(strategyName, project.ActiveStrategy, StringComparison.OrdinalIgnoreCase) ? "* " + strategyName : "  " + strategyName),
					Escape(definition?.Kind ?? "(unknown)"),
					Escape(definition?.Model ?? definition?.Command ?? definition?.BaseUrl ?? "(unset)"),
					string.Equals(strategyName, project.ActiveStrategy, StringComparison.OrdinalIgnoreCase) ? "active" : "ready");
			}
		}

		if (project.StrategyCount == 0)
		{
			table.AddRow("(none)", "(none)", "(unset)", "needs config");
		}

		console.Write(table);
		console.WriteLine($"Selected Strategy: {project.ActiveStrategy ?? "(none)"}");
		console.WriteLine("Actions: A Add strategy | E Edit selected | D Delete selected | S Set active | R Restore snapshot");
		console.WriteLine(": set-active <strategy> --project <project>    Ctrl+S Save    Esc Back");
	}

	private static void WriteCatalog(
		IAnsiConsole console,
		IReadOnlyList<AiUnitStrategyCatalogEntry> catalog,
		AiUnitWorkspaceDiscoveryResult discovery)
	{
		console.Write(new Rule("Strategy Catalog"));
		console.Write(new Rule("Reusable Strategies"));
		var grouped = catalog
			.GroupBy(entry => new
			{
				entry.StrategyName,
				entry.Definition.Kind,
				entry.Definition.Command,
				entry.Definition.Model,
				entry.Definition.BaseUrl,
				entry.Definition.ApiKeyEnvVar,
			})
			.OrderBy(group => group.Key.StrategyName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var table = new Table { Border = TableBorder.Ascii };
		table.AddColumn("Strategy");
		table.AddColumn("Source Projects");
		table.AddColumn("Status");
		foreach (var group in grouped)
		{
			var definition = group.First().Definition;
			table.AddRow(
				Escape($"{group.Key.StrategyName} {definition.Kind} {definition.Model ?? definition.Command ?? definition.BaseUrl ?? "(unset)"}"),
				group.Select(entry => entry.ProjectName).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(),
				string.IsNullOrWhiteSpace(definition.ApiKeyEnvVar) ? "ready" : "env var");
		}

		if (grouped.Length == 0)
		{
			table.AddRow("(none)", "0", "empty");
		}

		console.Write(table);
		console.WriteLine($"Apply Preview: target: {discovery.Projects.Count} projects selected");
		console.WriteLine("effect: add missing strategy; preserve active if already set");
		console.WriteLine("Catalog Actions: A Apply to selected | G Apply globally | N New template | C Clone strategy | X Resolve conflict");
		console.WriteLine(": apply-global codex --dry-run    Space Toggle Project    Enter Apply    Esc Back");
	}

	private static void WriteValidation(
		IAnsiConsole console,
		AiUnitWorkspaceDiscoveryResult discovery)
	{
		console.Write(new Rule("Validation and Deploy"));
		var table = new Table { Border = TableBorder.Ascii };
		table.AddColumn("Gate");
		table.AddColumn("State");
		table.AddColumn("Artifact");
		table.AddRow("discover fixture workspace", discovery.Projects.Count > 0 ? "PASS" : "WARN", "scan results");
		table.AddRow("validate strategy JSON", discovery.HasErrors ? "FAIL" : "PASS", "validate output");
		table.AddRow("capture TUI screenshots", "PEND", "docs/screenshots/aiunit-repl/*.png");
		table.AddRow("aiUnit wireframe comparisons", "PEND", "tests/.../WireframeComparisons");
		table.AddRow("dotnet tool pack/install smoke", "PEND", "SharpNinja.aiUnit.Tool");
		console.Write(table);
		console.WriteLine("Current Blocker: finished TUI screenshots wait on screenshot capture automation");
		console.WriteLine(": validate --all    R Run    O Open    P Pack    Ctrl+Q Quit");
	}

	private static string ScreenTitle(AiUnitTuiScreen screen) =>
		screen switch
		{
			AiUnitTuiScreen.Overview => "Workspace Overview",
			AiUnitTuiScreen.Projects => "Project Strategy Editor",
			AiUnitTuiScreen.Catalog => "Strategy Catalog",
			AiUnitTuiScreen.Validate => "Validation and Deploy",
			_ => screen.ToString(),
		};

	private static string Nav(string name, bool active) =>
		active ? "> " + name : "  " + name;

	private static string State(AiUnitDiscoveredProject project) =>
		project.Status switch
		{
			AiUnitDiscoveryStatus.Ok => "valid",
			AiUnitDiscoveryStatus.Warning => "needs config",
			AiUnitDiscoveryStatus.Error => "error",
			_ => project.Status.ToString().ToLowerInvariant(),
		};

	private static string Escape(string value) => Markup.Escape(value);
}
