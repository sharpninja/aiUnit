using System;
using System.Globalization;
using System.IO;

namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Resolves the effective results-output directory and online base URL for
/// review run logs, and builds sortable run-log file names. Precedence for
/// both values is: env var, then <see cref="AiUnitResultsOptions"/>, then the
/// built-in default (a local <c>aiunit-results</c> directory for the path; no
/// URL otherwise).
/// </summary>
public static class AiUnitResultsLocator
{
	/// <summary>Default directory name created under the test output base directory.</summary>
	public const string DefaultDirectoryName = "aiunit-results";

	/// <summary>Env var that overrides the configured output directory.</summary>
	public const string OutputDirEnvVar = "AIUNIT_RESULTS_DIR";

	/// <summary>Env var that overrides the configured online base URL.</summary>
	public const string OnlineBaseUrlEnvVar = "AIUNIT_RESULTS_BASE_URL";

	/// <summary>Sortable UTC timestamp format used in run-log file names.</summary>
	public const string TimestampFormat = "yyyyMMdd'T'HHmmss'.'fff'Z'";

	/// <summary>
	/// Resolves the directory where run-log result files are written:
	/// <see cref="OutputDirEnvVar"/>, then
	/// <see cref="AiUnitResultsOptions.OutputDirectory"/>, then
	/// <c>{AppContext.BaseDirectory}/aiunit-results</c>.
	/// </summary>
	public static string ResolveOutputDirectory(AiUnitResultsOptions? options)
	{
		var env = Environment.GetEnvironmentVariable(OutputDirEnvVar);
		if (!string.IsNullOrWhiteSpace(env))
		{
			return env!;
		}

		if (!string.IsNullOrWhiteSpace(options?.OutputDirectory))
		{
			return options!.OutputDirectory!;
		}

		return Path.Combine(AppContext.BaseDirectory, DefaultDirectoryName);
	}

	/// <summary>
	/// Resolves the online base URL for run-log links:
	/// <see cref="OnlineBaseUrlEnvVar"/>, then
	/// <see cref="AiUnitResultsOptions.OnlineBaseUrl"/>, then null.
	/// </summary>
	public static string? ResolveOnlineBaseUrl(AiUnitResultsOptions? options)
	{
		var env = Environment.GetEnvironmentVariable(OnlineBaseUrlEnvVar);
		if (!string.IsNullOrWhiteSpace(env))
		{
			return env;
		}

		return string.IsNullOrWhiteSpace(options?.OnlineBaseUrl) ? null : options!.OnlineBaseUrl;
	}

	/// <summary>
	/// Builds the run-log file name <c>aiunit-review-{type}-{yyyyMMddTHHmmss.fffZ}.json</c>
	/// using the UTC value of <paramref name="startedUtc"/>. The timestamp is
	/// the start of the test, making file names sort chronologically.
	/// </summary>
	public static string BuildFileName(string reviewType, DateTimeOffset startedUtc)
	{
		var safeType = string.IsNullOrWhiteSpace(reviewType) ? "review" : reviewType;
		var timestamp = startedUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
		return $"aiunit-review-{safeType}-{timestamp}.json";
	}
}
