using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Google Gemini Generative Language adapter (POST
/// /v1beta/models/{model}:generateContent?key={apiKey}). The API key is
/// passed as a query string (NOT a header) and is redacted from log lines.
/// JSON mode is requested via generationConfig.responseMimeType.
/// </summary>
/// <remarks>
/// Cost rates: gemini-2.5-pro approx $1.25/M input, $10/M output;
/// gemini-2.5-flash approx $0.30/M input, $2.50/M output (2026-04 list price).
/// Update from Google AI pricing page when revised.
/// </remarks>
public sealed class GeminiFrontierClient : FrontierClientBase, IFrontierModelClient
{
	/// <summary>Construct with injected HTTP factory, config, and logger.</summary>
	public GeminiFrontierClient(
		IHttpClientFactory httpClientFactory,
		IFrontierProviderConfig config,
		ILogger<GeminiFrontierClient> logger)
		: base(httpClientFactory, config, logger) { }

	/// <inheritdoc />
	public override string Provider => "google";

	/// <inheritdoc />
	protected override HttpRequestMessage BuildRequest(FrontierRequest request)
	{
		var body = SerializeBody(request);
		var path = $"/v1beta/models/{Uri.EscapeDataString(Config.ModelVersion)}:generateContent?key={Uri.EscapeDataString(Config.ApiKey)}";
		var endpoint = new Uri(Config.BaseUrl, path);
		var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		return msg;
	}

	/// <inheritdoc />
	protected override string SafeRequestUri(HttpRequestMessage request)
	{
		// Hide the api-key query param from logs.
		var uri = request.RequestUri;
		if (uri is null) return "<no-uri>";
		var path = uri.GetLeftPart(UriPartial.Path);
		return $"{path}?key=***";
	}

	/// <inheritdoc />
	protected override (string Text, FrontierTokenUsage Usage) ParseSuccessBody(string body)
	{
		using var doc = JsonDocument.Parse(body);
		var root = doc.RootElement;

		if (!root.TryGetProperty("candidates", out var candidates)
			|| candidates.ValueKind != JsonValueKind.Array
			|| candidates.GetArrayLength() == 0)
		{
			throw new JsonException("Missing or empty 'candidates' array.");
		}
		if (!candidates[0].TryGetProperty("content", out var content)
			|| !content.TryGetProperty("parts", out var parts)
			|| parts.ValueKind != JsonValueKind.Array
			|| parts.GetArrayLength() == 0)
		{
			throw new JsonException("Missing 'content.parts' array.");
		}

		var sb = new StringBuilder();
		foreach (var part in parts.EnumerateArray())
		{
			if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
			{
				sb.Append(t.GetString());
			}
		}
		if (sb.Length == 0)
		{
			throw new JsonException("No 'text' parts in candidate content.");
		}

		int input = 0, output = 0, total = 0;
		if (root.TryGetProperty("usageMetadata", out var usage) && usage.ValueKind == JsonValueKind.Object)
		{
			if (usage.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt32(out var pv)) input = pv;
			if (usage.TryGetProperty("candidatesTokenCount", out var c) && c.TryGetInt32(out var cv)) output = cv;
			if (usage.TryGetProperty("totalTokenCount", out var tt) && tt.TryGetInt32(out var ttv)) total = ttv;
			if (total == 0) total = input + output;
		}
		return (sb.ToString(), new FrontierTokenUsage(input, output, total));
	}

	/// <inheritdoc />
	protected override decimal? EstimateCostUsd(FrontierTokenUsage usage)
	{
		var (inRate, outRate) = Config.ModelVersion switch
		{
			var m when m.Contains("2.5-pro", StringComparison.OrdinalIgnoreCase) => (0.00125m, 0.01m),
			var m when m.Contains("2.5-flash", StringComparison.OrdinalIgnoreCase) => (0.0003m, 0.0025m),
			var m when m.Contains("1.5-pro", StringComparison.OrdinalIgnoreCase) => (0.00125m, 0.005m),
			var m when m.Contains("1.5-flash", StringComparison.OrdinalIgnoreCase) => (0.000075m, 0.0003m),
			_ => (0.00125m, 0.01m),
		};
		return Math.Round(
			(usage.InputTokens / 1000m) * inRate + (usage.OutputTokens / 1000m) * outRate,
			6,
			MidpointRounding.AwayFromZero);
	}

	private static string SerializeBody(FrontierRequest request)
	{
		using var ms = new MemoryStream();
		using (var writer = new Utf8JsonWriter(ms))
		{
			writer.WriteStartObject();

			// systemInstruction is optional - only emit if non-empty.
			if (!string.IsNullOrEmpty(request.SystemPrompt))
			{
				writer.WriteStartObject("systemInstruction");
				writer.WriteStartArray("parts");
				writer.WriteStartObject();
				writer.WriteString("text", request.SystemPrompt);
				writer.WriteEndObject();
				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			writer.WriteStartArray("contents");
			writer.WriteStartObject();
			writer.WriteString("role", "user");
			writer.WriteStartArray("parts");

			// Vision + auxiliary text attachments. Images become inlineData
			// parts; text attachments inline as additional text parts. The user
			// message is the trailing text part.
			var attachments = request.Attachments ?? Array.Empty<FrontierAttachment>();
			foreach (var att in attachments)
			{
				if (att is null) continue;
				if (att.IsImage)
				{
					writer.WriteStartObject();
					writer.WriteStartObject("inlineData");
					writer.WriteString("mimeType", att.MediaType);
					writer.WriteString("data", Convert.ToBase64String(att.Data ?? Array.Empty<byte>()));
					writer.WriteEndObject();
					writer.WriteEndObject();
				}
				else
				{
					writer.WriteStartObject();
					writer.WriteString("text", Encoding.UTF8.GetString(att.Data ?? Array.Empty<byte>()));
					writer.WriteEndObject();
				}
			}

			writer.WriteStartObject();
			writer.WriteString("text", request.UserMessage ?? string.Empty);
			writer.WriteEndObject();
			writer.WriteEndArray();
			writer.WriteEndObject();
			writer.WriteEndArray();

			// generationConfig: only emit when at least one knob is set.
			if (request.MaxTokens.HasValue || request.Temperature.HasValue || request.RequireJsonOutput)
			{
				writer.WriteStartObject("generationConfig");
				if (request.MaxTokens is int max)
				{
					writer.WriteNumber("maxOutputTokens", max);
				}
				if (request.Temperature is double temp)
				{
					writer.WriteNumber("temperature", temp);
				}
				if (request.RequireJsonOutput)
				{
					writer.WriteString("responseMimeType", "application/json");
				}
				writer.WriteEndObject();
			}

			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}
}
