using System.Text.Json.Serialization;

namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Optional results-output configuration loaded from the <c>AiUnit.Results</c>
/// section of <c>appsettings.aiunit.json</c>. Controls where review run-log
/// result files are written and the base URL used to build online run-log
/// links. Both fields are optional; env vars
/// <c>AIUNIT_RESULTS_DIR</c> and <c>AIUNIT_RESULTS_BASE_URL</c> override them.
/// </summary>
/// <param name="OutputDirectory">
/// Directory where run-log result files are written. When null/empty the
/// default <c>aiunit-results</c> folder under the test output base directory
/// is used. See <see cref="AiUnitResultsLocator"/>.
/// </param>
/// <param name="OnlineBaseUrl">
/// Optional base URL for an online run-log store. When set, the run-log
/// reference embedded in review JSON includes a <c>url</c> formed by joining
/// this base with the run-log file name.
/// </param>
public sealed record AiUnitResultsOptions(
	[property: JsonPropertyName("OutputDirectory")] string? OutputDirectory = null,
	[property: JsonPropertyName("OnlineBaseUrl")] string? OnlineBaseUrl = null);
