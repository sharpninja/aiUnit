using System;
using System.Collections.Generic;
using System.IO;

namespace SharpNinja.AiUnit.Scenarios;

/// <summary>
/// Generic walker that locates a consumer-named marker directory by climbing
/// parent directories from <see cref="AppContext.BaseDirectory"/> (or an
/// explicit <paramref name="startDirectory"/> override), then enumerates
/// matching files inside that marker folder and hands each (path, text)
/// pair to a consumer-supplied loader. The aiUnit library deliberately does
/// NOT take a YamlDotNet dependency - the consumer plugs in whatever parser
/// it prefers via the <see cref="Func{T1, T2, TResult}"/> argument.
/// </summary>
public static class AiUnitScenarioCatalog
{
	/// <summary>
	/// Locates the marker directory by walking up from
	/// <paramref name="startDirectory"/> (default: <see cref="AppContext.BaseDirectory"/>),
	/// then enumerates all files matching <paramref name="fileGlob"/>
	/// directly inside it and projects each into a
	/// <typeparamref name="TScenario"/> via <paramref name="loader"/>.
	/// </summary>
	/// <typeparam name="TScenario">Caller-defined scenario record / DTO.</typeparam>
	/// <param name="markerFolderName">
	/// Folder name to find (e.g. <c>"wireframes"</c> or <c>"scenarios"</c>).
	/// </param>
	/// <param name="loader">
	/// Function invoked once per matched file with (absolutePath, fileText)
	/// returning the parsed scenario.
	/// </param>
	/// <param name="fileGlob">
	/// Glob pattern for the files inside the marker folder. Defaults to
	/// <c>"*.yaml"</c> but any glob the file system supports works.
	/// </param>
	/// <param name="startDirectory">
	/// Optional starting directory for the walk. Defaults to
	/// <see cref="AppContext.BaseDirectory"/>; the test suite uses this
	/// override to point at a temp directory.
	/// </param>
	/// <returns>
	/// All loaded scenarios. An empty list is returned when the marker
	/// directory is not found - callers decide whether to treat that as a
	/// skip or a failure.
	/// </returns>
	public static IReadOnlyList<TScenario> LoadAll<TScenario>(
		string markerFolderName,
		Func<string, string, TScenario> loader,
		string fileGlob = "*.yaml",
		string? startDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(markerFolderName);
		ArgumentNullException.ThrowIfNull(loader);
		if (string.IsNullOrEmpty(fileGlob))
		{
			fileGlob = "*.yaml";
		}

		var markerPath = LocateMarkerDirectory(markerFolderName, startDirectory: startDirectory);
		if (string.IsNullOrEmpty(markerPath) || !Directory.Exists(markerPath))
		{
			return Array.Empty<TScenario>();
		}

		var results = new List<TScenario>();
		foreach (var file in Directory.EnumerateFiles(markerPath, fileGlob, SearchOption.TopDirectoryOnly))
		{
			var text = File.ReadAllText(file);
			results.Add(loader(file, text));
		}
		return results;
	}

	/// <summary>
	/// Climbs parent directories starting at <paramref name="startDirectory"/>
	/// (default <see cref="AppContext.BaseDirectory"/>) looking for a child
	/// directory whose name equals <paramref name="markerFolderName"/>
	/// (case-insensitive). Returns the full path to that directory, or
	/// <see langword="null"/> when not found.
	/// </summary>
	/// <param name="markerFolderName">Folder name to find.</param>
	/// <param name="parentChainHints">
	/// Optional additional names of ancestor folders to recognize as
	/// "boundaries". When supplied, the walker will stop once it has climbed
	/// past one of these and the marker has not been found. Currently
	/// advisory only - pass <see langword="null"/> in the common case.
	/// </param>
	/// <param name="startDirectory">
	/// Optional start point for the walk. Defaults to
	/// <see cref="AppContext.BaseDirectory"/>.
	/// </param>
	/// <returns>Absolute marker path, or null when not found.</returns>
	public static string? LocateMarkerDirectory(
		string markerFolderName,
		string[]? parentChainHints = null,
		string? startDirectory = null)
	{
		ArgumentNullException.ThrowIfNull(markerFolderName);
		_ = parentChainHints; // reserved for future filtering
		var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, markerFolderName);
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
			// Also accept the case where the current directory's name MATCHES
			// the marker - useful when tests start INSIDE the marker folder.
			if (string.Equals(dir.Name, markerFolderName, StringComparison.OrdinalIgnoreCase))
			{
				return dir.FullName;
			}
			dir = dir.Parent;
		}
		return null;
	}
}
