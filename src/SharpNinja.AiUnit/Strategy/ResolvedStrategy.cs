namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Post-merge view of the strategy after env-var overrides are applied.
/// Used for telemetry / logging by the test runner.
/// </summary>
/// <param name="Name">Strategy name (e.g. "claude", "grok").</param>
/// <param name="Kind">Provider Kind ("anthropic", "openai-compatible", "gemini", "cli").</param>
/// <param name="BaseUrl">Resolved base URL (empty for cli kinds).</param>
/// <param name="Model">Resolved model identifier.</param>
/// <param name="TimeoutSeconds">Resolved timeout in seconds.</param>
/// <param name="Temperature">Resolved sampling temperature.</param>
public sealed record ResolvedStrategy(
	string Name,
	string Kind,
	string BaseUrl,
	string Model,
	int TimeoutSeconds,
	double Temperature);
