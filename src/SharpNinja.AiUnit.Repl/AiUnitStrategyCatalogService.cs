using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SharpNinja.AiUnit.Repl;

public sealed record AiUnitStrategyCatalogEntry(
	string StrategyName,
	AiUnitStrategyDefinition Definition,
	string ProjectName,
	string ConfigPath,
	bool IsActive);

public sealed record AiUnitStrategyApplyProjectResult(
	string ProjectName,
	string ConfigPath,
	bool Changed,
	bool Conflict,
	string Message,
	string? SnapshotPath);

public sealed record AiUnitStrategyApplyResult(
	bool DryRun,
	IReadOnlyList<AiUnitStrategyApplyProjectResult> Projects)
{
	public bool HasConflicts => Projects.Any(project => project.Conflict);

	public bool HasChanges => Projects.Any(project => project.Changed);
}

public sealed class AiUnitStrategyCatalogService
{
	private readonly AiUnitConfigSnapshotService _snapshotService;

	public AiUnitStrategyCatalogService(AiUnitConfigSnapshotService? snapshotService = null)
	{
		_snapshotService = snapshotService ?? new AiUnitConfigSnapshotService();
	}

	public IReadOnlyList<AiUnitStrategyCatalogEntry> Catalog(string rootPath)
	{
		var discovery = AiUnitWorkspaceDiscovery.Discover(rootPath);
		var entries = new List<AiUnitStrategyCatalogEntry>();

		foreach (var project in discovery.Projects)
		{
			if (project.ConfigPath is null || project.Status == AiUnitDiscoveryStatus.Error)
			{
				continue;
			}

			var editor = AiUnitStrategyConfigEditor.Load(project.ConfigPath);
			foreach (var strategyName in editor.StrategyNames)
			{
				if (!editor.TryGetStrategyDefinition(strategyName, out var definition) || definition is null)
				{
					continue;
				}

				entries.Add(new AiUnitStrategyCatalogEntry(
					strategyName,
					definition,
					project.ProjectName,
					project.ConfigPath,
					string.Equals(project.ActiveStrategy, strategyName, StringComparison.OrdinalIgnoreCase)));
			}
		}

		return entries
			.OrderBy(entry => entry.StrategyName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(entry => entry.ProjectName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public AiUnitStrategyApplyResult Apply(
		string rootPath,
		string strategyName,
		string? sourceProjectName,
		IReadOnlyCollection<string>? targetProjectNames,
		bool dryRun,
		bool force)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(strategyName);

		var discovery = AiUnitWorkspaceDiscovery.Discover(rootPath);
		var source = ResolveSource(discovery, strategyName, sourceProjectName);
		if (source is null)
		{
			return new AiUnitStrategyApplyResult(
				dryRun,
				new[]
				{
					new AiUnitStrategyApplyProjectResult(
						ProjectName: sourceProjectName ?? "(catalog)",
						ConfigPath: string.Empty,
						Changed: false,
						Conflict: true,
						Message: $"Strategy '{strategyName}' was not found in the discovered catalog.",
						SnapshotPath: null),
				});
		}

		var selectedTargetNames = targetProjectNames is null
			? null
			: new HashSet<string>(targetProjectNames, StringComparer.OrdinalIgnoreCase);
		var results = new List<AiUnitStrategyApplyProjectResult>();

		foreach (var project in discovery.Projects.OrderBy(project => project.ProjectName, StringComparer.OrdinalIgnoreCase))
		{
			if (selectedTargetNames is not null && !selectedTargetNames.Contains(project.ProjectName))
			{
				continue;
			}

			results.Add(ApplyToProject(project, source, dryRun, force));
		}

		if (selectedTargetNames is not null)
		{
			foreach (var missingTarget in selectedTargetNames
				.Where(name => discovery.FindProject(name) is null)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
			{
				results.Add(new AiUnitStrategyApplyProjectResult(
					missingTarget,
					string.Empty,
					Changed: false,
					Conflict: true,
					Message: $"Target project '{missingTarget}' was not found.",
					SnapshotPath: null));
			}
		}

		return new AiUnitStrategyApplyResult(dryRun, results);
	}

	private AiUnitStrategyCatalogEntry? ResolveSource(
		AiUnitWorkspaceDiscoveryResult discovery,
		string strategyName,
		string? sourceProjectName)
	{
		return Catalog(discovery.RootPath)
			.Where(entry => string.Equals(entry.StrategyName, strategyName, StringComparison.OrdinalIgnoreCase))
			.Where(entry => string.IsNullOrWhiteSpace(sourceProjectName)
				|| string.Equals(entry.ProjectName, sourceProjectName, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(entry => entry.IsActive)
			.ThenBy(entry => entry.ProjectName, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	private AiUnitStrategyApplyProjectResult ApplyToProject(
		AiUnitDiscoveredProject project,
		AiUnitStrategyCatalogEntry source,
		bool dryRun,
		bool force)
	{
		if (project.ConfigPath is null)
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				Path.Combine(project.ProjectDirectory, "appsettings.aiunit.json"),
				Changed: false,
				Conflict: false,
				Message: "Skipped: config file is missing.",
				SnapshotPath: null);
		}

		if (project.Status == AiUnitDiscoveryStatus.Error)
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				project.ConfigPath,
				Changed: false,
				Conflict: true,
				Message: "Skipped: project config has validation errors.",
				SnapshotPath: null);
		}

		var editor = AiUnitStrategyConfigEditor.Load(project.ConfigPath);
		var exists = editor.TryGetStrategyDefinition(source.StrategyName, out var existing);
		if (exists && existing == source.Definition)
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				project.ConfigPath,
				Changed: false,
				Conflict: false,
				Message: $"Strategy '{source.StrategyName}' is already up to date.",
				SnapshotPath: null);
		}

		if (exists && !force)
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				project.ConfigPath,
				Changed: false,
				Conflict: true,
				Message: $"Conflict: strategy '{source.StrategyName}' already exists with different settings. Use --force to overwrite.",
				SnapshotPath: null);
		}

		var action = exists ? "update" : "add";
		if (dryRun)
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				project.ConfigPath,
				Changed: true,
				Conflict: false,
				Message: $"Would {action} strategy '{source.StrategyName}'.",
				SnapshotPath: null);
		}

		var snapshot = _snapshotService.CreateSnapshot(project.ConfigPath);
		if (!editor.TryAddOrUpdateStrategy(source.StrategyName, source.Definition, out var error))
		{
			return new AiUnitStrategyApplyProjectResult(
				project.ProjectName,
				project.ConfigPath,
				Changed: false,
				Conflict: true,
				Message: error,
				SnapshotPath: snapshot.SnapshotPath);
		}

		editor.Save();
		return new AiUnitStrategyApplyProjectResult(
			project.ProjectName,
			project.ConfigPath,
			Changed: true,
			Conflict: false,
			Message: $"{char.ToUpperInvariant(action[0])}{action[1..]}ed strategy '{source.StrategyName}'.",
			SnapshotPath: snapshot.SnapshotPath);
	}
}
