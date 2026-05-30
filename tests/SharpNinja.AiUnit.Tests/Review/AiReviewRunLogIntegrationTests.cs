using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Review;
using SharpNinja.AiUnit.Strategy;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// Integration tests for FR-AIUNIT-024/025: the file-backed run-log sink
/// writes a real result file with a sortable name, the executor end-to-end
/// emits a review JSON whose runLog.path exists on disk, and the strategy
/// loader parses an appsettings file carrying an <c>AiUnit.Results</c> block.
/// </summary>
public sealed class AiReviewRunLogIntegrationTests
{
	private static string NewTempDir() =>
		Path.Combine(Path.GetTempPath(), "aiunit-itest-" + Guid.NewGuid().ToString("N"));

	[Fact]
	public void FileSink_WritesValidJsonFile_WithSortableName_AndOnlineUrl()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, "https://logs.example/runs");
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
			var entry = new AiReviewRunLogEntry(
				started,
				"code",
				new[] { "default" },
				"Prompt text",
				"fake",
				"fake-model",
				LatencyMs: 5,
				FrontierTokenUsage.Zero,
				Error: null,
				FindingsJson: """{"status":"pass","summary":"ok","findings":[]}""");

			var runLog = sink.Write(entry);

			Assert.True(File.Exists(runLog.Path));
			Assert.Equal("aiunit-review-code-20260530T120000.000Z.json", Path.GetFileName(runLog.Path));
			Assert.Equal(
				"https://logs.example/runs/aiunit-review-code-20260530T120000.000Z.json",
				runLog.Url);

			using var doc = JsonDocument.Parse(File.ReadAllText(runLog.Path));
			var root = doc.RootElement;
			Assert.Equal("code", root.GetProperty("reviewType").GetString());
			Assert.Equal("Prompt text", root.GetProperty("prompt").GetString());
			Assert.Equal("pass", root.GetProperty("findings").GetProperty("status").GetString());
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void FileSink_CollidingTimestamps_ProduceDistinctFiles()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, null);
			var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
			AiReviewRunLogEntry Entry() => new(
				started, "code", new[] { "default" }, "p", null, null, 0, FrontierTokenUsage.Zero, null,
				"""{"status":"pass","findings":[]}""");

			var first = sink.Write(Entry());
			var second = sink.Write(Entry());

			Assert.NotEqual(first.Path, second.Path);
			Assert.True(File.Exists(first.Path));
			Assert.True(File.Exists(second.Path));
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public async Task Executor_EndToEnd_WithFileSink_RunLogPathExistsOnDisk()
	{
		var dir = NewTempDir();
		try
		{
			var sink = new FileAiReviewRunLogSink(dir, null);
			var resolver = new StubReviewResolver();
			var request = new AiReviewExecutionRequest(
				AiReviewKind.Project, "Project prompt", Array.Empty<AiReviewAgentSpec>());

			var result = await AiReviewExecutor.ExecuteAsync(request, resolver, sink);

			using var doc = JsonDocument.Parse(result);
			var path = doc.RootElement.GetProperty("runLog").GetProperty("path").GetString();
			Assert.False(string.IsNullOrWhiteSpace(path));
			Assert.True(File.Exists(path));

			using var logged = JsonDocument.Parse(File.ReadAllText(path!));
			Assert.Equal("project", logged.RootElement.GetProperty("reviewType").GetString());
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void StrategyLoader_ParsesResultsSection()
	{
		var dir = NewTempDir();
		Directory.CreateDirectory(dir);
		var file = Path.Combine(dir, "appsettings.aiunit.json");
		try
		{
			File.WriteAllText(file, """
			{
			  "AiUnit": {
			    "ActiveStrategy": "claude",
			    "Results": { "OutputDirectory": "./out", "OnlineBaseUrl": "https://logs.example" },
			    "Strategies": { "claude": { "Kind": "cli", "Command": "claude" } }
			  }
			}
			""");

			var cfg = AiUnitStrategyLoader.TryLoad(file);

			Assert.NotNull(cfg);
			Assert.NotNull(cfg!.Results);
			Assert.Equal("./out", cfg.Results!.OutputDirectory);
			Assert.Equal("https://logs.example", cfg.Results.OnlineBaseUrl);
		}
		finally
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
	}
}
