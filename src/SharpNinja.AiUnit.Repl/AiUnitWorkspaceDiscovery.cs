using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;

namespace SharpNinja.AiUnit.Repl;

public enum AiUnitDiscoveryStatus
{
	Ok,
	Warning,
	Error,
}

public sealed record AiUnitDiscoveredProject(
	string ProjectName,
	string ProjectPath,
	string ProjectDirectory,
	string? ConfigPath,
	bool HasPackageReference,
	bool HasProjectReference,
	string? ActiveStrategy,
	IReadOnlyList<string> StrategyNames,
	AiUnitDiscoveryStatus Status,
	IReadOnlyList<string> Messages)
{
	public int StrategyCount => StrategyNames.Count;

	public string ConfigDisplay => ConfigPath ?? "(missing)";

	public string ReferenceSummary
	{
		get
		{
			var references = new List<string>(capacity: 2);
			if (HasPackageReference)
			{
				references.Add("package");
			}

			if (HasProjectReference)
			{
				references.Add("project");
			}

			return references.Count == 0 ? "config-only" : string.Join("+", references);
		}
	}
}

public sealed record AiUnitWorkspaceDiscoveryResult(
	string RootPath,
	IReadOnlyList<AiUnitDiscoveredProject> Projects)
{
	public bool HasErrors => Projects.Any(project => project.Status == AiUnitDiscoveryStatus.Error);

	public AiUnitDiscoveredProject? FindProject(string projectName) =>
		Projects.FirstOrDefault(project =>
			string.Equals(project.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
}

public static class AiUnitWorkspaceDiscovery
{
	private const string ConfigFileName = "appsettings.aiunit.json";
	private const string PackageId = "SharpNinja.aiUnit";
	private const string LibraryProjectFileName = "SharpNinja.AiUnit.csproj";

	public static AiUnitWorkspaceDiscoveryResult Discover(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		var root = Path.GetFullPath(rootPath);
		if (!Directory.Exists(root))
		{
			return new AiUnitWorkspaceDiscoveryResult(root, Array.Empty<AiUnitDiscoveredProject>());
		}

		var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
			.Where(path => !IsUnderExcludedDirectory(root, path))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.Select(TryDiscoverProject)
			.Where(project => project is not null)
			.Cast<AiUnitDiscoveredProject>()
			.ToArray();

		return new AiUnitWorkspaceDiscoveryResult(root, projects);
	}

	private static AiUnitDiscoveredProject? TryDiscoverProject(string projectPath)
	{
		var directory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
		var configPath = Path.Combine(directory, ConfigFileName);
		var configExists = File.Exists(configPath);
		var projectName = Path.GetFileNameWithoutExtension(projectPath);
		var messages = new List<string>();
		var status = AiUnitDiscoveryStatus.Ok;
		var hasPackageReference = false;
		var hasProjectReference = false;

		try
		{
			var document = XDocument.Load(projectPath);
			projectName = ProjectName(document, projectName);
			hasPackageReference = document.Descendants("PackageReference")
				.Any(element => string.Equals(
					element.Attribute("Include")?.Value,
					PackageId,
					StringComparison.OrdinalIgnoreCase));
			hasProjectReference = document.Descendants("ProjectReference")
				.Any(element => string.Equals(
					Path.GetFileName(element.Attribute("Include")?.Value),
					LibraryProjectFileName,
					StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
		{
			status = AiUnitDiscoveryStatus.Error;
			messages.Add($"Project file could not be read: {ex.Message}");
		}

		if (!hasPackageReference && !hasProjectReference && !configExists)
		{
			return null;
		}

		var config = configExists
			? ReadConfig(configPath)
			: AiUnitConfigReadResult.Missing(configPath);

		status = Worst(status, config.Status);
		messages.AddRange(config.Messages);

		return new AiUnitDiscoveredProject(
			projectName,
			Path.GetFullPath(projectPath),
			directory,
			configExists ? Path.GetFullPath(configPath) : null,
			hasPackageReference,
			hasProjectReference,
			config.ActiveStrategy,
			config.StrategyNames,
			status,
			messages);
	}

	private static string ProjectName(XContainer document, string fallback)
	{
		var assemblyName = document.Descendants("AssemblyName")
			.Select(element => element.Value)
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
		return assemblyName ?? fallback;
	}

	private static AiUnitConfigReadResult ReadConfig(string configPath)
	{
		try
		{
			using var document = JsonDocument.Parse(
				File.ReadAllText(configPath),
				new JsonDocumentOptions
				{
					AllowTrailingCommas = true,
					CommentHandling = JsonCommentHandling.Skip,
				});

			if (!document.RootElement.TryGetProperty("AiUnit", out var root))
			{
				return AiUnitConfigReadResult.Error(
					configPath,
					activeStrategy: null,
					Array.Empty<string>(),
					"Config is missing required AiUnit object.");
			}

			var activeStrategy = root.TryGetProperty("ActiveStrategy", out var activeElement)
				&& activeElement.ValueKind == JsonValueKind.String
					? activeElement.GetString()
					: null;

			if (!root.TryGetProperty("Strategies", out var strategiesElement)
				|| strategiesElement.ValueKind != JsonValueKind.Object)
			{
				return AiUnitConfigReadResult.Error(
					configPath,
					activeStrategy,
					Array.Empty<string>(),
					"Config is missing required AiUnit.Strategies object.");
			}

			var strategyNames = strategiesElement.EnumerateObject()
				.Select(property => property.Name)
				.ToArray();
			var duplicateNames = strategyNames
				.GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
				.Where(group => group.Count() > 1)
				.Select(group => group.First())
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var distinctStrategyNames = strategyNames
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var messages = new List<string>();
			var status = AiUnitDiscoveryStatus.Ok;

			foreach (var duplicateName in duplicateNames)
			{
				messages.Add($"Duplicate strategy name '{duplicateName}'.");
				status = AiUnitDiscoveryStatus.Error;
			}

			if (string.IsNullOrWhiteSpace(activeStrategy))
			{
				messages.Add("ActiveStrategy is not configured.");
				status = Worst(status, AiUnitDiscoveryStatus.Warning);
			}
			else if (!distinctStrategyNames.Contains(activeStrategy, StringComparer.OrdinalIgnoreCase))
			{
				messages.Add($"Active strategy '{activeStrategy}' is not defined in Strategies.");
				status = Worst(status, AiUnitDiscoveryStatus.Warning);
			}

			if (distinctStrategyNames.Length == 0)
			{
				messages.Add("No strategies are configured.");
				status = Worst(status, AiUnitDiscoveryStatus.Warning);
			}

			return new AiUnitConfigReadResult(
				status,
				activeStrategy,
				distinctStrategyNames,
				messages);
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return AiUnitConfigReadResult.Error(
				configPath,
				activeStrategy: null,
				Array.Empty<string>(),
				$"Malformed config: {ex.Message}");
		}
	}

	private static bool IsUnderExcludedDirectory(string root, string path)
	{
		var relative = Path.GetRelativePath(root, path);
		var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return segments.Any(segment =>
			string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
	}

	private static AiUnitDiscoveryStatus Worst(
		AiUnitDiscoveryStatus left,
		AiUnitDiscoveryStatus right) =>
		(AiUnitDiscoveryStatus)Math.Max((int)left, (int)right);

	private sealed record AiUnitConfigReadResult(
		AiUnitDiscoveryStatus Status,
		string? ActiveStrategy,
		IReadOnlyList<string> StrategyNames,
		IReadOnlyList<string> Messages)
	{
		public static AiUnitConfigReadResult Missing(string configPath) =>
			new(
				AiUnitDiscoveryStatus.Warning,
				null,
				Array.Empty<string>(),
				new[] { $"Config file '{configPath}' was not found." });

		public static AiUnitConfigReadResult Error(
			string configPath,
			string? activeStrategy,
			IReadOnlyList<string> strategyNames,
			string message) =>
			new(
				AiUnitDiscoveryStatus.Error,
				activeStrategy,
				strategyNames,
				new[] { message });
	}
}
