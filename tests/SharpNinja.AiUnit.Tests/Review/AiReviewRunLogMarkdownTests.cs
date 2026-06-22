using System;
using System.IO;
using System.Text.Json;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Review;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// BDP-first acceptance tests for the new ReviewLog markdown behavior:
/// <list type="bullet">
/// <item>The file sink writes a human-readable Markdown companion next to the
/// JSON run log, with a mirrored file name (same stem, <c>.md</c> extension).</item>
/// <item><see cref="AiReviewRunLogRef"/> carries the local markdown path
/// (<c>MarkdownPath</c>); there is no online markdown URL.</item>
/// <item><see cref="AiReviewJson.InjectRunLog"/> injects <c>markdownPath</c> into
/// the review JSON <c>runLog</c> object when present, and omits it when null.</item>
/// </list>
/// Extends FR-AIUNIT-024 / TR-AIUNIT-REVIEW-009/010 (run-log reference + persistence).
/// </summary>
public sealed class AiReviewRunLogMarkdownTests
{
	private static string NewTempDir() =>
		Path.Combine(Path.GetTempPath(), "aiunit-md-" + Guid.NewGuid().ToString("N"));

	private static AiReviewRunLogEntry SampleEntry(
		DateTimeOffset started,
		string? error = null,
		string findingsJson = """{"status":"pass","summary":"looks good","findings":[]}""") =>
		new(
			started,
			"code",
			new[] { "default" },
			"Prompt text",
			"fake",
			"fake-model",
			LatencyMs: 42,
			new FrontierTokenUsage(InputTokens: 12, OutputTokens: 7, TotalTokens: 19),
			Error: error,
			FindingsJson: findingsJson);

	[Fact]
	public void FileSink_Write_AlsoWritesCompanionMarkdown_WithMirroredName_AndPopulatesMarkdownPath()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, "https://logs.example/runs");
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

			var runLog = sink.Write(SampleEntry(started));

			// JSON run log still written exactly as before.
			Assert.True(File.Exists(runLog.Path));
			Assert.Equal("aiunit-review-code-20260530T120000.000Z.json", Path.GetFileName(runLog.Path));

			// Companion markdown: mirrored stem, .md extension, local path on the ref.
			Assert.False(string.IsNullOrWhiteSpace(runLog.MarkdownPath));
			Assert.Equal("aiunit-review-code-20260530T120000.000Z.md", Path.GetFileName(runLog.MarkdownPath!));
			Assert.True(File.Exists(runLog.MarkdownPath!));
			Assert.Equal(
				Path.ChangeExtension(runLog.Path, ".md"),
				runLog.MarkdownPath);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void CompanionMarkdown_RendersReviewMetadataAndFindings()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, null);
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

			var runLog = sink.Write(SampleEntry(started));
			var md = File.ReadAllText(runLog.MarkdownPath!);

			// It is a Markdown document (leads with a heading).
			Assert.StartsWith("#", md.TrimStart(), StringComparison.Ordinal);
			// Metadata is present and human-readable.
			Assert.Contains("code", md, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("default", md, StringComparison.Ordinal);
			Assert.Contains("Prompt text", md, StringComparison.Ordinal);
			Assert.Contains("fake-model", md, StringComparison.Ordinal);
			Assert.Contains("42", md, StringComparison.Ordinal);   // latency
			Assert.Contains("19", md, StringComparison.Ordinal);   // total tokens
			// Findings rendered as a fenced JSON block carrying the status.
			Assert.Contains("```json", md, StringComparison.Ordinal);
			Assert.Contains("pass", md, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void CompanionMarkdown_ErrorRun_IncludesErrorText()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, null);
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
			const string error = "agent '__missing__' did not resolve";

			var runLog = sink.Write(SampleEntry(
				started,
				error: error,
				findingsJson: $$"""{"status":"error","summary":"{{error}}","findings":[]}"""));
			var md = File.ReadAllText(runLog.MarkdownPath!);

			Assert.Contains(error, md, StringComparison.Ordinal);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void FileSink_CollidingTimestamps_MarkdownMirrorsResolvedJsonStem()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, null);
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

			var first = sink.Write(SampleEntry(started));
			var second = sink.Write(SampleEntry(started));

			// Each markdown companion mirrors the stem of its own JSON sibling.
			Assert.Equal(Path.ChangeExtension(first.Path, ".md"), first.MarkdownPath);
			Assert.Equal(Path.ChangeExtension(second.Path, ".md"), second.MarkdownPath);
			Assert.NotEqual(first.MarkdownPath, second.MarkdownPath);
			Assert.True(File.Exists(first.MarkdownPath!));
			Assert.True(File.Exists(second.MarkdownPath!));
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void InjectRunLog_AddsMarkdownPath_WhenPresent()
	{
		const string json = """{"status":"pass","findings":[]}""";
		var runLog = new AiReviewRunLogRef(
			Path: @"C:\runs\run.json",
			Url: null,
			StartedUtc: new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero),
			MarkdownPath: @"C:\runs\run.md");

		var result = AiReviewJson.InjectRunLog(json, runLog);

		using var doc = JsonDocument.Parse(result);
		var injected = doc.RootElement.GetProperty("runLog");
		Assert.Equal(@"C:\runs\run.json", injected.GetProperty("path").GetString());
		Assert.Equal(@"C:\runs\run.md", injected.GetProperty("markdownPath").GetString());
	}

	[Fact]
	public void InjectRunLog_OmitsMarkdownPath_WhenNull()
	{
		const string json = """{"status":"pass","findings":[]}""";
		var runLog = new AiReviewRunLogRef("/tmp/run.json", null, DateTimeOffset.UtcNow, MarkdownPath: null);

		var result = AiReviewJson.InjectRunLog(json, runLog);

		using var doc = JsonDocument.Parse(result);
		var injected = doc.RootElement.GetProperty("runLog");
		Assert.False(injected.TryGetProperty("markdownPath", out _));
	}
}
