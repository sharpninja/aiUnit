using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SharpNinja.AiUnit.Review;

/// <summary>
/// Renders a <see cref="AiReviewRunLogEntry"/> as a human-readable Markdown
/// document. The Markdown companion is written next to the JSON run log by
/// <see cref="FileAiReviewRunLogSink"/> so reviewers can read a run transcript
/// without parsing JSON. The JSON file remains the canonical machine record.
/// </summary>
internal static class AiReviewRunLogMarkdown
{
	/// <summary>Renders the run-log entry as Markdown.</summary>
	public static string Render(AiReviewRunLogEntry entry)
	{
		var sb = new StringBuilder();

		sb.Append("# aiUnit ").Append(entry.ReviewType).AppendLine(" review run log");
		sb.AppendLine();

		sb.Append("- **Started (UTC):** ")
			.AppendLine(entry.StartedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
		sb.Append("- **Review type:** ").AppendLine(entry.ReviewType);
		sb.Append("- **Agents:** ").AppendLine(JoinAgents(entry.Agents));
		if (!string.IsNullOrWhiteSpace(entry.Provider))
		{
			sb.Append("- **Provider:** ").AppendLine(entry.Provider);
		}
		if (!string.IsNullOrWhiteSpace(entry.Model))
		{
			sb.Append("- **Model:** ").AppendLine(entry.Model);
		}
		sb.Append("- **Latency:** ")
			.Append(entry.LatencyMs.ToString(CultureInfo.InvariantCulture))
			.AppendLine(" ms");
		sb.Append("- **Tokens:** ")
			.Append(entry.TokenUsage.InputTokens.ToString(CultureInfo.InvariantCulture))
			.Append(" in / ")
			.Append(entry.TokenUsage.OutputTokens.ToString(CultureInfo.InvariantCulture))
			.Append(" out / ")
			.Append(entry.TokenUsage.TotalTokens.ToString(CultureInfo.InvariantCulture))
			.AppendLine(" total");
		sb.AppendLine();

		if (!string.IsNullOrWhiteSpace(entry.Error))
		{
			sb.AppendLine("## Error");
			sb.AppendLine();
			sb.AppendLine(entry.Error);
			sb.AppendLine();
		}

		sb.AppendLine("## Prompt");
		sb.AppendLine();
		AppendFenced(sb, "text", entry.Prompt);

		sb.AppendLine("## Findings");
		sb.AppendLine();
		AppendFenced(sb, "json", PrettyOrRaw(entry.FindingsJson));

		return sb.ToString();
	}

	private static string JoinAgents(System.Collections.Generic.IReadOnlyList<string> agents)
	{
		if (agents is null || agents.Count == 0)
		{
			return "(default)";
		}
		return string.Join(", ", agents);
	}

	private static void AppendFenced(StringBuilder sb, string language, string body)
	{
		sb.Append("```").AppendLine(language);
		sb.AppendLine(body);
		sb.AppendLine("```");
		sb.AppendLine();
	}

	private static string PrettyOrRaw(string json)
	{
		try
		{
			using var doc = JsonDocument.Parse(json);
			return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
		}
		catch (JsonException)
		{
			return json;
		}
	}
}
