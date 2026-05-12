using System;
using System.Globalization;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Cli;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Strategy;

/// <summary>
/// Builds an <see cref="IFrontierModelClient"/> from a
/// <see cref="AiUnitStrategySettings"/>. Kind dispatches to the matching
/// adapter:
///   "anthropic"           -&gt; <see cref="ClaudeFrontierClient"/>
///   "openai-compatible"   -&gt; <see cref="OpenAiCompatibleFrontierClient"/>
///                              with provider name = strategy name
///   "gemini"              -&gt; <see cref="GeminiFrontierClient"/>
///   "cli"                 -&gt; <see cref="CliFrontierClient"/>
/// Any other Kind returns a null client + skip reason. Env-var overrides
/// land on top of JSON config for: BaseUrl, Model, Timeout, Temperature,
/// Kind, Command, and the resolved API key.
/// </summary>
public static class AiUnitStrategyResolver
{
	/// <summary>Anthropic Messages API kind.</summary>
	public const string KindAnthropic = "anthropic";

	/// <summary>OpenAI-compatible Chat Completions kind (OpenAI, xAI, MAF, Cline, etc.).</summary>
	public const string KindOpenAiCompatible = "openai-compatible";

	/// <summary>Google Gemini Generative Language kind.</summary>
	public const string KindGemini = "gemini";

	/// <summary>Local CLI binary kind (reuses operator subscription auth).</summary>
	public const string KindCli = "cli";

	/// <summary>
	/// Builds the active client from the supplied strategy settings.
	/// </summary>
	/// <param name="strategyName">Logical strategy name used in telemetry.</param>
	/// <param name="settings">JSON-loaded settings, or null when only env vars apply.</param>
	/// <param name="httpClientFactory">
	/// Optional factory for HTTP adapters; defaults to <see cref="AiUnitHttpClientFactory"/>.
	/// </param>
	public static (IFrontierModelClient? Client, ResolvedStrategy Resolved, string SkipReason) Build(
		string strategyName,
		AiUnitStrategySettings? settings,
		IHttpClientFactory? httpClientFactory = null)
	{
		// Env-var overrides land on top of JSON config so an operator can
		// pivot to a different endpoint / model / timeout without editing
		// appsettings.aiunit.json (matches CI / scripted-run ergonomics).
		var baseUrlRaw = Environment.GetEnvironmentVariable("AIUNIT_BASE_URL")
			?? settings?.BaseUrl
			?? string.Empty;
		var model = Environment.GetEnvironmentVariable("AIUNIT_MODEL")
			?? settings?.Model
			?? string.Empty;
		var timeoutSeconds = TryParseInt(
			Environment.GetEnvironmentVariable("AIUNIT_TIMEOUT_SECONDS"),
			settings?.TimeoutSeconds ?? 600);
		var temperature = TryParseDouble(
			Environment.GetEnvironmentVariable("AIUNIT_TEMPERATURE"),
			settings?.Temperature ?? 0.0);
		var kind = (Environment.GetEnvironmentVariable("AIUNIT_KIND")
			?? settings?.Kind
			?? string.Empty).Trim().ToLowerInvariant();

		var resolved = new ResolvedStrategy(
			Name: strategyName,
			Kind: kind,
			BaseUrl: baseUrlRaw,
			Model: model,
			TimeoutSeconds: timeoutSeconds,
			Temperature: temperature);

		if (string.IsNullOrWhiteSpace(kind))
		{
			return (null, resolved, $"Strategy '{strategyName}' has no Kind configured");
		}

		// Kind=cli short-circuits the API key + URL gate. The CLI handles
		// auth out-of-band (operator's existing subscription). Required
		// field for this Kind is Command.
		if (string.Equals(kind, KindCli, StringComparison.OrdinalIgnoreCase))
		{
			var command = Environment.GetEnvironmentVariable("AIUNIT_COMMAND")
				?? settings?.Command
				?? string.Empty;
			if (string.IsNullOrWhiteSpace(command))
			{
				return (null, resolved, $"Strategy '{strategyName}': Kind=cli requires Command field or AIUNIT_COMMAND env var");
			}
			IFrontierModelClient cliClient = new CliFrontierClient(
				providerName: $"{strategyName}:{command}",
				command: command,
				timeout: TimeSpan.FromSeconds(timeoutSeconds),
				logger: NullLogger<CliFrontierClient>.Instance,
				modelVersion: string.IsNullOrEmpty(model) ? null : model);
			return (cliClient, resolved with { Model = string.IsNullOrEmpty(model) ? "(cli-managed)" : model }, string.Empty);
		}

		if (string.IsNullOrWhiteSpace(baseUrlRaw))
		{
			return (null, resolved, $"Strategy '{strategyName}' has no BaseUrl configured");
		}
		if (string.IsNullOrWhiteSpace(model))
		{
			return (null, resolved, $"Strategy '{strategyName}' has no Model configured");
		}

		// API key resolution: strategy-named env var first, fall back to the
		// shared override env var. Either may carry the key in CI.
		string? apiKey = null;
		if (!string.IsNullOrEmpty(settings?.ApiKeyEnvVar))
		{
			apiKey = Environment.GetEnvironmentVariable(settings!.ApiKeyEnvVar);
		}
		if (string.IsNullOrEmpty(apiKey))
		{
			apiKey = Environment.GetEnvironmentVariable("AIUNIT_API_KEY");
		}
		if (string.IsNullOrEmpty(apiKey))
		{
			var envName = settings?.ApiKeyEnvVar ?? "AIUNIT_API_KEY";
			return (null, resolved, $"Strategy '{strategyName}': no API key found in '{envName}' or AIUNIT_API_KEY");
		}

		if (!Uri.TryCreate(baseUrlRaw, UriKind.Absolute, out var baseUri))
		{
			return (null, resolved, $"Strategy '{strategyName}': BaseUrl '{baseUrlRaw}' is not a valid absolute URL");
		}

		var providerConfig = new FrontierProviderConfig(
			ApiKey: apiKey,
			ModelVersion: model,
			BaseUrl: baseUri,
			Timeout: TimeSpan.FromSeconds(timeoutSeconds));
		var factory = httpClientFactory ?? new AiUnitHttpClientFactory();

		IFrontierModelClient? client = kind switch
		{
			KindAnthropic => new ClaudeFrontierClient(
				factory, providerConfig, NullLogger<ClaudeFrontierClient>.Instance),
			KindOpenAiCompatible => new OpenAiCompatibleFrontierClient(
				factory, providerConfig, providerName: strategyName, NullLogger<OpenAiCompatibleFrontierClient>.Instance),
			KindGemini => new GeminiFrontierClient(
				factory, providerConfig, NullLogger<GeminiFrontierClient>.Instance),
			_ => null,
		};

		if (client is null)
		{
			return (null, resolved, $"Strategy '{strategyName}': unknown Kind '{kind}'");
		}
		return (client, resolved, string.Empty);
	}

	private static int TryParseInt(string? value, int fallback)
	{
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed : fallback;
	}

	private static double TryParseDouble(string? value, double fallback)
	{
		if (string.IsNullOrWhiteSpace(value)) return fallback;
		return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			? parsed : fallback;
	}
}
