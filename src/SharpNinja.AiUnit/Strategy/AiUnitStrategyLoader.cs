using System;
using System.IO;
using System.Text.Json;

namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Loads <see cref="AiUnitStrategyConfig"/> from
/// <c>appsettings.aiunit.json</c> and resolves the active strategy per the
/// documented precedence order.
/// </summary>
public static class AiUnitStrategyLoader
{
	private const string DefaultStrategy = "claude";
	private const string FileName = "appsettings.aiunit.json";

	/// <summary>
	/// Loads <c>appsettings.aiunit.json</c> from the test or consumer process's
	/// output directory. Walks up the directory tree from
	/// <see cref="AppContext.BaseDirectory"/> until the file is found.
	/// Returns null when the file is missing or unparseable - the caller
	/// falls back to env-var-only configuration.
	/// </summary>
	/// <param name="overridePath">Optional absolute path to load instead of probing.</param>
	public static AiUnitStrategyConfig? TryLoad(string? overridePath = null)
	{
		try
		{
			var path = overridePath ?? ProbeForConfigFile();
			if (path is null || !File.Exists(path)) return null;

			var json = File.ReadAllText(path);
			using var doc = JsonDocument.Parse(json,
				new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
			if (!doc.RootElement.TryGetProperty("AiUnit", out var root))
			{
				return null;
			}
			return JsonSerializer.Deserialize<AiUnitStrategyConfig>(root.GetRawText(),
				new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					ReadCommentHandling = JsonCommentHandling.Skip,
					AllowTrailingCommas = true,
				});
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Resolves which strategy is active per the documented precedence:
	/// <c>AIUNIT_STRATEGY</c> env var, then the JSON
	/// <c>ActiveStrategy</c>, then the <c>"claude"</c> fallback. Returns
	/// (name, settings) where settings is null when the resolved name has
	/// no matching entry in the strategies map.
	/// </summary>
	/// <param name="config">Loaded config or null if no JSON file was found.</param>
	public static (string Name, AiUnitStrategySettings? Settings) ResolveActive(AiUnitStrategyConfig? config)
	{
		var envName = Environment.GetEnvironmentVariable("AIUNIT_STRATEGY");
		var name = !string.IsNullOrWhiteSpace(envName)
			? envName!
			: (config?.ActiveStrategy ?? DefaultStrategy);

		if (config?.Strategies is { } strategies && strategies.TryGetValue(name, out var cfg))
		{
			return (name, cfg);
		}
		return (name, null);
	}

	/// <summary>
	/// Walks up from <see cref="AppContext.BaseDirectory"/> looking for
	/// <c>appsettings.aiunit.json</c>. Returns the absolute path, or null
	/// when no file is found before the filesystem root.
	/// </summary>
	private static string? ProbeForConfigFile()
	{
		var direct = Path.Combine(AppContext.BaseDirectory, FileName);
		if (File.Exists(direct)) return direct;

		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, FileName);
			if (File.Exists(candidate)) return candidate;
			dir = dir.Parent;
		}
		return null;
	}
}
