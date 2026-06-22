using System;
using System.IO;
using System.Text;
using System.Text.Json;
using SharpNinja.AiUnit.Strategy;

namespace SharpNinja.AiUnit.Review;

/// <summary>
/// File-backed <see cref="IAiReviewRunLogSink"/> that writes each review run as
/// a JSON document into the configured results directory. The file name embeds
/// the sortable UTC start time of the test (see
/// <see cref="AiUnitResultsLocator.BuildFileName"/>); colliding timestamps get
/// an incrementing suffix.
/// </summary>
internal sealed class FileAiReviewRunLogSink : IAiReviewRunLogSink
{
	/// <summary>Run-log document schema identifier.</summary>
	public const string RunLogSchemaVersion = "aiunit.review.runlog.v1";

	private readonly string _directory;
	private readonly string? _onlineBaseUrl;

	public FileAiReviewRunLogSink(string directory, string? onlineBaseUrl)
	{
		_directory = directory;
		_onlineBaseUrl = onlineBaseUrl;
	}

	/// <summary>
	/// Builds a sink from <c>appsettings.aiunit.json</c> + env-var overrides.
	/// Used by the production review path when no explicit sink is supplied.
	/// </summary>
	public static FileAiReviewRunLogSink FromConfig()
	{
		var results = AiUnitStrategyLoader.TryLoad()?.Results;
		var directory = AiUnitResultsLocator.ResolveOutputDirectory(results);
		var url = AiUnitResultsLocator.ResolveOnlineBaseUrl(results);
		return new FileAiReviewRunLogSink(directory, url);
	}

	/// <inheritdoc />
	public AiReviewRunLogRef Write(AiReviewRunLogEntry entry)
	{
		Directory.CreateDirectory(_directory);

		var fileName = AiUnitResultsLocator.BuildFileName(entry.ReviewType, entry.StartedUtc);
		var path = ResolveNonCollidingPath(fileName);
		File.WriteAllText(path, Serialize(entry));

		// Human-readable Markdown companion: same stem, .md extension. The JSON
		// file remains the canonical machine record; the companion is best-effort
		// alongside it. The path mirrors the resolved (collision-safe) JSON stem.
		var markdownPath = Path.ChangeExtension(path, ".md");
		File.WriteAllText(markdownPath, AiReviewRunLogMarkdown.Render(entry));

		var url = _onlineBaseUrl is null
			? null
			: CombineUrl(_onlineBaseUrl, Path.GetFileName(path));
		return new AiReviewRunLogRef(path, url, entry.StartedUtc, markdownPath);
	}

	private string ResolveNonCollidingPath(string fileName)
	{
		var path = Path.Combine(_directory, fileName);
		if (!File.Exists(path))
		{
			return path;
		}

		var stem = Path.GetFileNameWithoutExtension(fileName);
		var ext = Path.GetExtension(fileName);
		for (var n = 1; ; n++)
		{
			var candidate = Path.Combine(_directory, $"{stem}-{n}{ext}");
			if (!File.Exists(candidate))
			{
				return candidate;
			}
		}
	}

	private static string Serialize(AiReviewRunLogEntry entry)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			writer.WriteString("schemaVersion", RunLogSchemaVersion);
			writer.WriteString("startedUtc", entry.StartedUtc.ToUniversalTime().ToString("o"));
			writer.WriteString("reviewType", entry.ReviewType);

			writer.WriteStartArray("agents");
			foreach (var agent in entry.Agents)
			{
				writer.WriteStringValue(agent);
			}
			writer.WriteEndArray();

			writer.WriteString("prompt", entry.Prompt);
			if (!string.IsNullOrWhiteSpace(entry.Provider)) writer.WriteString("provider", entry.Provider);
			if (!string.IsNullOrWhiteSpace(entry.Model)) writer.WriteString("model", entry.Model);
			writer.WriteNumber("latencyMs", entry.LatencyMs);

			writer.WriteStartObject("tokenUsage");
			writer.WriteNumber("inputTokens", entry.TokenUsage.InputTokens);
			writer.WriteNumber("outputTokens", entry.TokenUsage.OutputTokens);
			writer.WriteNumber("totalTokens", entry.TokenUsage.TotalTokens);
			writer.WriteEndObject();

			if (!string.IsNullOrWhiteSpace(entry.Error)) writer.WriteString("error", entry.Error);

			writer.WritePropertyName("findings");
			WriteFindings(writer, entry.FindingsJson);

			writer.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void WriteFindings(Utf8JsonWriter writer, string findingsJson)
	{
		try
		{
			using var doc = JsonDocument.Parse(findingsJson);
			doc.RootElement.WriteTo(writer);
		}
		catch (JsonException)
		{
			writer.WriteStringValue(findingsJson);
		}
	}

	private static string CombineUrl(string baseUrl, string fileName) =>
		$"{baseUrl.TrimEnd('/')}/{fileName}";
}
