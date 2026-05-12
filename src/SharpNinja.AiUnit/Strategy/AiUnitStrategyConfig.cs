using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Strategy-based aiUnit configuration. Loaded from
/// <c>appsettings.aiunit.json</c> in the test project's output directory.
/// Each strategy declares the provider Kind, endpoint, model, API-key env
/// var, and per-request defaults. The active strategy is selected by:
///   1. <c>AIUNIT_STRATEGY</c> env var (highest priority)
///   2. <c>ActiveStrategy</c> in the JSON file
///   3. Hard-coded fallback "claude"
/// Per-strategy fields can be overridden individually via env vars:
///   <c>AIUNIT_BASE_URL</c>, <c>AIUNIT_MODEL</c>, <c>AIUNIT_API_KEY</c>,
///   <c>AIUNIT_TIMEOUT_SECONDS</c>, <c>AIUNIT_TEMPERATURE</c>,
///   <c>AIUNIT_KIND</c>, <c>AIUNIT_COMMAND</c>.
/// </summary>
/// <param name="ActiveStrategy">Name of the default strategy to use.</param>
/// <param name="Strategies">Named map of every configured strategy.</param>
public sealed record AiUnitStrategyConfig(
	[property: JsonPropertyName("ActiveStrategy")] string ActiveStrategy,
	[property: JsonPropertyName("Strategies")] IReadOnlyDictionary<string, AiUnitStrategySettings> Strategies);

/// <summary>
/// Per-strategy settings deserialized from the JSON config file. All fields
/// are optional except Kind; the resolver enforces required fields per Kind.
/// </summary>
/// <param name="Kind">Provider Kind: anthropic | openai-compatible | gemini | cli.</param>
/// <param name="BaseUrl">HTTP base URL (HTTP kinds only).</param>
/// <param name="Model">Model identifier (e.g. "claude-opus-4-5", "gpt-5-codex").</param>
/// <param name="ApiKeyEnvVar">Name of the env var carrying the API key (HTTP kinds).</param>
/// <param name="Command">CLI binary name (cli kind only).</param>
/// <param name="TimeoutSeconds">Per-call timeout in seconds (default 600).</param>
/// <param name="Temperature">Sampling temperature (default 0.0).</param>
/// <param name="Description">Human-readable description shown in logs.</param>
public sealed record AiUnitStrategySettings(
	[property: JsonPropertyName("Kind")] string Kind,
	[property: JsonPropertyName("BaseUrl")] string? BaseUrl = null,
	[property: JsonPropertyName("Model")] string? Model = null,
	[property: JsonPropertyName("ApiKeyEnvVar")] string? ApiKeyEnvVar = null,
	[property: JsonPropertyName("Command")] string? Command = null,
	[property: JsonPropertyName("TimeoutSeconds")] int TimeoutSeconds = 600,
	[property: JsonPropertyName("Temperature")] double Temperature = 0.0,
	[property: JsonPropertyName("Description")] string? Description = null);
