using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Consolidated OpenAI-compatible Chat Completions adapter (POST
/// /v1/chat/completions). Single class handles OpenAI, xAI, MAF, Cline, and
/// any other provider that speaks the OpenAI surface. Differentiation is by
/// constructor-supplied provider name + dynamic cost-rate lookup keyed on
/// model prefix.
/// </summary>
/// <remarks>
/// Cost rate table (USD per 1K tokens):
/// <list type="bullet">
///   <item><c>gpt-5-codex</c> -> $0.005 input / $0.015 output</item>
///   <item><c>gpt-5*</c> -> $0.005 input / $0.015 output</item>
///   <item><c>gpt-4o-mini*</c> -> $0.00015 input / $0.0006 output</item>
///   <item><c>gpt-4o*</c> -> $0.0025 input / $0.01 output</item>
///   <item><c>gpt-4*</c> -> $0.03 input / $0.06 output</item>
///   <item><c>grok-4*</c> -> $0.005 input / $0.015 output</item>
///   <item><c>grok-3*</c> -> $0.003 input / $0.015 output</item>
///   <item>fallback -> $0.005 input / $0.015 output</item>
/// </list>
/// </remarks>
public sealed class OpenAiCompatibleFrontierClient : FrontierClientBase, IFrontierModelClient
{
	private readonly string _provider;

	/// <summary>Construct with provider name (e.g. "openai", "xai", "maf", "cline").</summary>
	public OpenAiCompatibleFrontierClient(
		IHttpClientFactory httpClientFactory,
		IFrontierProviderConfig config,
		string providerName,
		ILogger<OpenAiCompatibleFrontierClient> logger)
		: base(httpClientFactory, config, logger)
	{
		if (string.IsNullOrWhiteSpace(providerName))
		{
			throw new ArgumentException("Provider name is required.", nameof(providerName));
		}
		_provider = providerName.Trim().ToLowerInvariant();
	}

	/// <inheritdoc />
	public override string Provider => _provider;

	/// <inheritdoc />
	protected override HttpRequestMessage BuildRequest(FrontierRequest request)
	{
		var body = OpenAiCompatibleSerializer.Serialize(Config.ModelVersion, request);
		var endpoint = new Uri(Config.BaseUrl, "/v1/chat/completions");
		var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Config.ApiKey);
		return msg;
	}

	/// <inheritdoc />
	protected override (string Text, FrontierTokenUsage Usage) ParseSuccessBody(string body)
		=> OpenAiCompatibleSerializer.ParseResponse(body);

	/// <inheritdoc />
	protected override decimal? EstimateCostUsd(FrontierTokenUsage usage)
	{
		var (inRate, outRate) = LookupRates(Config.ModelVersion);
		return Math.Round(
			(usage.InputTokens / 1000m) * inRate + (usage.OutputTokens / 1000m) * outRate,
			6,
			MidpointRounding.AwayFromZero);
	}

	/// <summary>
	/// Static cost-rate lookup keyed on model prefix. The match order matters:
	/// the most-specific prefix must come first (codex before gpt-5, mini
	/// before plain 4o).
	/// </summary>
	private static (decimal InRate, decimal OutRate) LookupRates(string modelVersion)
	{
		if (string.IsNullOrEmpty(modelVersion))
		{
			return (0.005m, 0.015m);
		}
		// OpenAI Codex family - same rate table as gpt-5 base. Codex pricing
		// matches base gpt-5 today; this branch exists so a future divergence
		// only edits one tuple.
		if (modelVersion.StartsWith("gpt-5-codex", StringComparison.OrdinalIgnoreCase))
		{
			return (0.005m, 0.015m);
		}
		if (modelVersion.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
		{
			return (0.005m, 0.015m);
		}
		if (modelVersion.StartsWith("gpt-4o-mini", StringComparison.OrdinalIgnoreCase))
		{
			return (0.00015m, 0.0006m);
		}
		if (modelVersion.StartsWith("gpt-4o", StringComparison.OrdinalIgnoreCase))
		{
			return (0.0025m, 0.01m);
		}
		if (modelVersion.StartsWith("gpt-4", StringComparison.OrdinalIgnoreCase))
		{
			return (0.03m, 0.06m);
		}
		if (modelVersion.Contains("grok-4", StringComparison.OrdinalIgnoreCase))
		{
			return (0.005m, 0.015m);
		}
		if (modelVersion.Contains("grok-3", StringComparison.OrdinalIgnoreCase))
		{
			return (0.003m, 0.015m);
		}
		return (0.005m, 0.015m);
	}
}
