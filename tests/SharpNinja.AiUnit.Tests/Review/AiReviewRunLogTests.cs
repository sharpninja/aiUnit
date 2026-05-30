using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Review;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// Unit tests for FR-AIUNIT-024 (review JSON includes run-log reference) and
/// TR-AIUNIT-REVIEW-009/010. Covers <see cref="AiReviewJson.InjectRunLog"/>
/// and the executor wiring on every output path using an in-memory sink.
/// </summary>
public sealed class AiReviewRunLogTests
{
	[Fact]
	public void InjectRunLog_AddsRunLogObject_AndPreservesExistingProperties()
	{
		var json = """
		{"schemaVersion":"aiunit.review.findings.v1","reviewType":"code","status":"pass","summary":"ok","agent":{"name":"a"},"findings":[]}
		""";
		var startedUtc = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
		var runLog = new AiReviewRunLogRef(
			@"C:\runs\aiunit-review-code-20260530T120000.000Z.json",
			"https://logs.example/runs/abc",
			startedUtc);

		var result = AiReviewJson.InjectRunLog(json, runLog);

		using var doc = JsonDocument.Parse(result);
		var root = doc.RootElement;
		Assert.Equal("pass", root.GetProperty("status").GetString());
		Assert.Equal("ok", root.GetProperty("summary").GetString());
		Assert.Equal("a", root.GetProperty("agent").GetProperty("name").GetString());
		var injected = root.GetProperty("runLog");
		Assert.Equal(runLog.Path, injected.GetProperty("path").GetString());
		Assert.Equal("https://logs.example/runs/abc", injected.GetProperty("url").GetString());
		Assert.True(injected.TryGetProperty("startedUtc", out _));
	}

	[Fact]
	public void InjectRunLog_OmitsUrl_WhenNull()
	{
		var json = """{"status":"pass","findings":[]}""";
		var runLog = new AiReviewRunLogRef("/tmp/run.json", null, DateTimeOffset.UtcNow);

		var result = AiReviewJson.InjectRunLog(json, runLog);

		using var doc = JsonDocument.Parse(result);
		var injected = doc.RootElement.GetProperty("runLog");
		Assert.Equal("/tmp/run.json", injected.GetProperty("path").GetString());
		Assert.False(injected.TryGetProperty("url", out _));
	}

	[Fact]
	public void InjectRunLog_ReplacesExistingRunLog()
	{
		var json = """{"status":"pass","runLog":{"path":"old"},"findings":[]}""";
		var runLog = new AiReviewRunLogRef("new", null, DateTimeOffset.UtcNow);

		var result = AiReviewJson.InjectRunLog(json, runLog);

		using var doc = JsonDocument.Parse(result);
		Assert.Equal("new", doc.RootElement.GetProperty("runLog").GetProperty("path").GetString());
	}

	[Fact]
	public void InjectRunLog_NonObjectRoot_ReturnsOriginal()
	{
		const string json = "[1,2,3]";
		var runLog = new AiReviewRunLogRef("p", null, DateTimeOffset.UtcNow);

		Assert.Equal(json, AiReviewJson.InjectRunLog(json, runLog));
	}

	[Fact]
	public async Task Executor_SingleAgent_WritesRunLogEntry_AndInjectsPath()
	{
		var resolver = new StubReviewResolver();
		var sink = new RecordingRunLogSink(@"C:\runs\file.json", "https://logs.example/file");
		var request = new AiReviewExecutionRequest(AiReviewKind.Code, "Review this.", Array.Empty<AiReviewAgentSpec>());

		var result = await AiReviewExecutor.ExecuteAsync(request, resolver, sink);

		var entry = Assert.Single(sink.Entries);
		Assert.Equal("code", entry.ReviewType);
		Assert.Equal("Review this.", entry.Prompt);
		using var doc = JsonDocument.Parse(result);
		var injected = doc.RootElement.GetProperty("runLog");
		Assert.Equal(@"C:\runs\file.json", injected.GetProperty("path").GetString());
		Assert.Equal("https://logs.example/file", injected.GetProperty("url").GetString());
	}

	[Fact]
	public async Task Executor_WrappedError_StillInjectsRunLog()
	{
		var resolver = new StubReviewResolver();
		var sink = new RecordingRunLogSink(@"C:\runs\err.json", null);
		var request = new AiReviewExecutionRequest(
			AiReviewKind.Plan,
			"Plan prompt",
			new[] { new AiReviewAgentSpec(Name: "missing") });

		var result = await AiReviewExecutor.ExecuteAsync(request, resolver, sink);

		using var doc = JsonDocument.Parse(result);
		Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
		Assert.Equal(@"C:\runs\err.json", doc.RootElement.GetProperty("runLog").GetProperty("path").GetString());
		var entry = Assert.Single(sink.Entries);
		Assert.False(string.IsNullOrWhiteSpace(entry.Error));
	}

	[Fact]
	public async Task Executor_MultiAgentAggregate_InjectsRunLog()
	{
		var resolver = new StubReviewResolver();
		resolver.Clients["agent-a"] = new StubReviewClient("agent-a", _ => ReviewJsonSamples.Pass("agent-a"));
		resolver.Clients["agent-b"] = new StubReviewClient("agent-b", _ => ReviewJsonSamples.Pass("agent-b"));
		var sink = new RecordingRunLogSink(@"C:\runs\agg.json", null);
		var request = new AiReviewExecutionRequest(
			AiReviewKind.Code,
			"Review the diff.",
			new[] { new AiReviewAgentSpec(Name: "agent-a"), new AiReviewAgentSpec(Name: "agent-b") });

		var result = await AiReviewExecutor.ExecuteAsync(request, resolver, sink);

		using var doc = JsonDocument.Parse(result);
		Assert.Equal(@"C:\runs\agg.json", doc.RootElement.GetProperty("runLog").GetProperty("path").GetString());
		Assert.Single(sink.Entries);
	}

	[Fact]
	public void FindingsSchema_DeclaresOptionalRunLog()
	{
		using var doc = JsonDocument.Parse(AiReviewFindingsSchema.JsonSchema);
		var root = doc.RootElement;

		Assert.True(root.GetProperty("properties").TryGetProperty("runLog", out var runLog));
		Assert.Equal("object", runLog.GetProperty("type").GetString());
		var required = root.GetProperty("required").EnumerateArray().Select(static x => x.GetString()).ToList();
		Assert.DoesNotContain("runLog", required);
	}
}
