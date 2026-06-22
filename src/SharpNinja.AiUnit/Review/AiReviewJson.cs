using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SharpNinja.AiUnit.Review;

internal static class AiReviewJson
{
	public static string NormalizeOrWrap(
		AiReviewKind kind,
		string agent,
		string? text,
		string? error = null,
		string? provider = null,
		string? model = null,
		IReadOnlyList<AiReviewAgentResult>? agentReviews = null)
	{
		if (string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(text))
		{
			var trimmed = text.Trim();
			if (AiReviewFindingsValidator.TryValidate(trimmed, out var validationError))
			{
				return trimmed;
			}

			error = $"Review response failed aiUnit findings schema validation: {validationError}";
		}

		var summary = string.IsNullOrWhiteSpace(error)
			? "Review agent returned empty or non-JSON output."
			: error;

		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
		{
			writer.WriteStartObject();
			writer.WriteString("schemaVersion", AiReviewFindingsSchema.SchemaVersion);
			writer.WriteString("reviewType", AiReviewPrompts.ReviewTypeName(kind));
			writer.WriteString("status", "error");
			writer.WriteString("summary", summary);
			writer.WriteStartObject("agent");
			writer.WriteString("name", agent);
			if (!string.IsNullOrWhiteSpace(provider)) writer.WriteString("provider", provider);
			if (!string.IsNullOrWhiteSpace(model)) writer.WriteString("model", model);
			writer.WriteEndObject();
			writer.WriteStartArray("findings");
			writer.WriteStartObject();
			writer.WriteString("severity", "high");
			writer.WriteString("category", "review-execution");
			writer.WriteString("title", "Review did not produce schema-valid findings JSON");
			writer.WriteString("detail", string.IsNullOrWhiteSpace(text)
				? summary
				: $"{summary}\n\nRaw response:\n{text.Trim()}");
			writer.WriteString("recommendation", "Check the configured aiUnit review agent and retry the review.");
			writer.WriteString("agent", agent);
			writer.WriteEndObject();
			writer.WriteEndArray();
			WriteAgentReviews(writer, agentReviews);
			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	/// <summary>
	/// Returns <paramref name="reviewJson"/> with a <c>runLog</c> property added
	/// (replacing any existing one), preserving all other properties. The
	/// property carries the local run-log path, optional online URL, optional
	/// local Markdown companion path, and the UTC start time. When the root is
	/// not a JSON object the input is returned unchanged.
	/// </summary>
	public static string InjectRunLog(string reviewJson, AiReviewRunLogRef runLog)
	{
		try
		{
			using var doc = JsonDocument.Parse(reviewJson);
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return reviewJson;
			}

			using var stream = new MemoryStream();
			using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
			{
				writer.WriteStartObject();
				foreach (var property in doc.RootElement.EnumerateObject())
				{
					if (property.NameEquals("runLog"))
					{
						continue;
					}
					property.WriteTo(writer);
				}

				writer.WriteStartObject("runLog");
				writer.WriteString("path", runLog.Path);
				if (!string.IsNullOrWhiteSpace(runLog.Url))
				{
					writer.WriteString("url", runLog.Url);
				}
				if (!string.IsNullOrWhiteSpace(runLog.MarkdownPath))
				{
					writer.WriteString("markdownPath", runLog.MarkdownPath);
				}
				writer.WriteString("startedUtc", runLog.StartedUtc.ToUniversalTime().ToString("o"));
				writer.WriteEndObject();

				writer.WriteEndObject();
			}
			return Encoding.UTF8.GetString(stream.ToArray());
		}
		catch (JsonException)
		{
			return reviewJson;
		}
	}

	public static string BuildAgentReviewsJson(IReadOnlyList<AiReviewAgentResult> reviews)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
		{
			WriteAgentReviewsArray(writer, reviews);
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static bool IsJsonObject(string text)
	{
		try
		{
			using var doc = JsonDocument.Parse(text);
			return doc.RootElement.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static void WriteAgentReviews(Utf8JsonWriter writer, IReadOnlyList<AiReviewAgentResult>? reviews)
	{
		if (reviews is not { Count: > 0 })
		{
			return;
		}
		writer.WritePropertyName("agentReviews");
		WriteAgentReviewsArray(writer, reviews);
	}

	private static void WriteAgentReviewsArray(Utf8JsonWriter writer, IReadOnlyList<AiReviewAgentResult> reviews)
	{
		writer.WriteStartArray();
		foreach (var review in reviews)
		{
			writer.WriteStartObject();
			writer.WriteString("agent", review.Agent);
			writer.WritePropertyName("result");
			using var doc = JsonDocument.Parse(review.ResultJson);
			doc.RootElement.WriteTo(writer);
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
	}
}
