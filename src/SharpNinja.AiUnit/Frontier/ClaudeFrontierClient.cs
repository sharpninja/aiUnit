using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Anthropic Messages API adapter (POST /v1/messages). Auth header is
/// <c>x-api-key</c> plus the required <c>anthropic-version</c> header. JSON
/// mode is requested by appending an instruction to the system prompt
/// (Anthropic does not have a structured response_format setting in v1).
/// Cost rates: claude-opus-4-x approx $15/M input, $75/M output;
/// claude-sonnet-4-x approx $3/M input, $15/M output (2026-04 list price).
/// </summary>
public sealed class ClaudeFrontierClient : FrontierClientBase, IFrontierModelClient
{
	private const string AnthropicVersion = "2023-06-01";
	private const int DefaultMaxTokens = 1024;
	private const string JsonInstruction = " Output your response as a single JSON object with no surrounding prose.";

	/// <summary>Construct with injected HTTP factory, config, and logger.</summary>
	public ClaudeFrontierClient(
		IHttpClientFactory httpClientFactory,
		IFrontierProviderConfig config,
		ILogger<ClaudeFrontierClient> logger)
		: base(httpClientFactory, config, logger) { }

	/// <inheritdoc />
	public override string Provider => "anthropic";

	/// <inheritdoc />
	protected override HttpRequestMessage BuildRequest(FrontierRequest request)
	{
		var body = SerializeBody(Config.ModelVersion, request);
		var endpoint = new Uri(Config.BaseUrl, "/v1/messages");
		var msg = new HttpRequestMessage(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json"),
		};
		msg.Headers.Add("x-api-key", Config.ApiKey);
		msg.Headers.Add("anthropic-version", AnthropicVersion);
		return msg;
	}

	/// <inheritdoc />
	protected override (string Text, FrontierTokenUsage Usage) ParseSuccessBody(string body)
	{
		using var doc = JsonDocument.Parse(body);
		var root = doc.RootElement;

		if (!root.TryGetProperty("content", out var content)
			|| content.ValueKind != JsonValueKind.Array
			|| content.GetArrayLength() == 0)
		{
			throw new JsonException("Missing or empty 'content' array.");
		}

		// Concatenate every "text"-typed block (Anthropic may return multiple
		// blocks if the assistant interleaves tool_use; v1 ignores tool blocks).
		var sb = new StringBuilder();
		foreach (var block in content.EnumerateArray())
		{
			if (block.TryGetProperty("type", out var type)
				&& type.ValueKind == JsonValueKind.String
				&& type.GetString() == "text"
				&& block.TryGetProperty("text", out var textProp)
				&& textProp.ValueKind == JsonValueKind.String)
			{
				sb.Append(textProp.GetString());
			}
		}
		if (sb.Length == 0)
		{
			throw new JsonException("No text block in 'content'.");
		}

		int input = 0, output = 0;
		if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
		{
			if (usage.TryGetProperty("input_tokens", out var i) && i.TryGetInt32(out var iv)) input = iv;
			if (usage.TryGetProperty("output_tokens", out var o) && o.TryGetInt32(out var ov)) output = ov;
		}
		return (sb.ToString(), new FrontierTokenUsage(input, output, input + output));
	}

	/// <inheritdoc />
	protected override decimal? EstimateCostUsd(FrontierTokenUsage usage)
	{
		var (inRate, outRate) = Config.ModelVersion switch
		{
			var m when m.Contains("opus-4", StringComparison.OrdinalIgnoreCase) => (0.015m, 0.075m),
			var m when m.Contains("sonnet-4", StringComparison.OrdinalIgnoreCase) => (0.003m, 0.015m),
			var m when m.Contains("haiku-4", StringComparison.OrdinalIgnoreCase) => (0.0008m, 0.004m),
			var m when m.Contains("opus", StringComparison.OrdinalIgnoreCase) => (0.015m, 0.075m),
			var m when m.Contains("sonnet", StringComparison.OrdinalIgnoreCase) => (0.003m, 0.015m),
			var m when m.Contains("haiku", StringComparison.OrdinalIgnoreCase) => (0.00025m, 0.00125m),
			_ => (0.015m, 0.075m),
		};
		return Math.Round(
			(usage.InputTokens / 1000m) * inRate + (usage.OutputTokens / 1000m) * outRate,
			6,
			MidpointRounding.AwayFromZero);
	}

	private static string SerializeBody(string modelVersion, FrontierRequest request)
	{
		using var ms = new MemoryStream();
		using (var writer = new Utf8JsonWriter(ms))
		{
			writer.WriteStartObject();
			writer.WriteString("model", modelVersion);

			var system = request.SystemPrompt ?? string.Empty;
			if (request.RequireJsonOutput)
			{
				system = string.IsNullOrEmpty(system) ? JsonInstruction.TrimStart() : system + JsonInstruction;
			}
			writer.WriteString("system", system);

			writer.WriteNumber("max_tokens", request.MaxTokens ?? DefaultMaxTokens);
			if (request.Temperature is double temp)
			{
				writer.WriteNumber("temperature", temp);
			}

			writer.WriteStartArray("messages");
			writer.WriteStartObject();
			writer.WriteString("role", "user");

			var images = (request.Attachments ?? Array.Empty<FrontierAttachment>())
				.Where(a => a is not null && a.IsImage)
				.ToList();
			var textAtts = (request.Attachments ?? Array.Empty<FrontierAttachment>())
				.Where(a => a is not null && !a.IsImage)
				.ToList();

			if (images.Count == 0)
			{
				writer.WriteString("content", BuildTextContent(request.UserMessage, textAtts));
			}
			else
			{
				writer.WriteStartArray("content");
				foreach (var img in images)
				{
					writer.WriteStartObject();
					writer.WriteString("type", "image");
					writer.WriteStartObject("source");
					writer.WriteString("type", "base64");
					writer.WriteString("media_type", img.MediaType);
					writer.WriteString("data", Convert.ToBase64String(img.Data ?? Array.Empty<byte>()));
					writer.WriteEndObject();
					writer.WriteEndObject();
				}
				writer.WriteStartObject();
				writer.WriteString("type", "text");
				writer.WriteString("text", BuildTextContent(request.UserMessage, textAtts));
				writer.WriteEndObject();
				writer.WriteEndArray();
			}

			writer.WriteEndObject();
			writer.WriteEndArray();

			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}

	/// <summary>
	/// Concatenates any text-typed attachments as fenced code blocks ahead of
	/// the user message body. Shared with the OpenAI-compatible + Gemini text
	/// inlining path - keeps the prompt body shape identical across providers
	/// for a given (UserMessage, Attachments) pair.
	/// </summary>
	internal static string BuildTextContent(string? userMessage, IReadOnlyList<FrontierAttachment> textAttachments)
	{
		if (textAttachments is null || textAttachments.Count == 0)
		{
			return userMessage ?? string.Empty;
		}
		var sb = new StringBuilder();
		foreach (var att in textAttachments)
		{
			if (att is null) continue;
			var lang = LanguageHintForMediaType(att.MediaType);
			sb.Append("```");
			if (!string.IsNullOrEmpty(lang)) sb.Append(lang);
			if (!string.IsNullOrEmpty(att.Name)) sb.Append(' ').Append(att.Name);
			sb.Append('\n');
			sb.Append(Encoding.UTF8.GetString(att.Data ?? Array.Empty<byte>()));
			if (sb.Length == 0 || sb[^1] != '\n') sb.Append('\n');
			sb.Append("```\n\n");
		}
		sb.Append(userMessage ?? string.Empty);
		return sb.ToString();
	}

	private static string LanguageHintForMediaType(string? mediaType) => mediaType switch
	{
		null => string.Empty,
		"application/json" or "text/json" => "json",
		"application/yaml" or "text/yaml" or "application/x-yaml" => "yaml",
		"image/svg+xml" => "xml",
		"text/markdown" => "markdown",
		"application/xml" or "text/xml" => "xml",
		"text/csv" => "csv",
		"text/html" => "html",
		"text/plain" => string.Empty,
		_ => string.Empty,
	};
}
