using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Internal helper shared by every OpenAI-compatible adapter. Encodes a
/// <see cref="FrontierRequest"/> as the canonical /v1/chat/completions JSON
/// body and parses the symmetric response shape.
/// </summary>
internal static class OpenAiCompatibleSerializer
{
	public static string Serialize(string modelVersion, FrontierRequest request)
	{
		using var ms = new MemoryStream();
		using (var writer = new Utf8JsonWriter(ms))
		{
			writer.WriteStartObject();
			writer.WriteString("model", modelVersion);

			writer.WriteStartArray("messages");
			writer.WriteStartObject();
			writer.WriteString("role", "system");
			writer.WriteString("content", request.SystemPrompt ?? string.Empty);
			writer.WriteEndObject();
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
				writer.WriteString("content", ClaudeFrontierClient.BuildTextContent(request.UserMessage, textAtts));
			}
			else
			{
				writer.WriteStartArray("content");
				writer.WriteStartObject();
				writer.WriteString("type", "text");
				writer.WriteString("text", ClaudeFrontierClient.BuildTextContent(request.UserMessage, textAtts));
				writer.WriteEndObject();
				foreach (var img in images)
				{
					writer.WriteStartObject();
					writer.WriteString("type", "image_url");
					writer.WriteStartObject("image_url");
					var b64 = Convert.ToBase64String(img.Data ?? Array.Empty<byte>());
					writer.WriteString("url", $"data:{img.MediaType};base64,{b64}");
					writer.WriteEndObject();
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
			}

			writer.WriteEndObject();
			writer.WriteEndArray();

			if (request.MaxTokens is int max)
			{
				writer.WriteNumber("max_tokens", max);
			}
			if (request.Temperature is double temp)
			{
				writer.WriteNumber("temperature", temp);
			}
			if (request.RequireJsonOutput)
			{
				writer.WriteStartObject("response_format");
				writer.WriteString("type", "json_object");
				writer.WriteEndObject();
			}
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(ms.ToArray());
	}

	public static (string Text, FrontierTokenUsage Usage) ParseResponse(string body)
	{
		using var doc = JsonDocument.Parse(body);
		var root = doc.RootElement;

		// choices[0].message.content
		if (!root.TryGetProperty("choices", out var choices)
			|| choices.ValueKind != JsonValueKind.Array
			|| choices.GetArrayLength() == 0)
		{
			throw new JsonException("Missing or empty 'choices' array.");
		}
		if (!choices[0].TryGetProperty("message", out var message)
			|| !message.TryGetProperty("content", out var content)
			|| content.ValueKind != JsonValueKind.String)
		{
			throw new JsonException("Missing 'message.content' string.");
		}
		var text = content.GetString() ?? string.Empty;

		int input = 0, output = 0, total = 0;
		if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
		{
			if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptv)) input = ptv;
			if (usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out var ctv)) output = ctv;
			if (usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out var ttv)) total = ttv;
			if (total == 0) total = input + output;
		}

		return (text, new FrontierTokenUsage(input, output, total));
	}
}
