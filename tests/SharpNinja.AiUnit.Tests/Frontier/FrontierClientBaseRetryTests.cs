using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Three-case coverage for retry + error-normalisation pipeline shared across
/// every adapter. Uses ClaudeFrontierClient as the concrete impl; the
/// behaviour is inherited from FrontierClientBase.
/// </summary>
public class FrontierClientBaseRetryTests
{
	private static FrontierProviderConfig Config() => new(
		ApiKey: "test-key",
		ModelVersion: "claude-opus-4-5",
		BaseUrl: new Uri("https://api.anthropic.com"),
		Timeout: TimeSpan.FromSeconds(15));

	private const string SuccessBody = """
	{
		"id": "msg_1",
		"type": "message",
		"role": "assistant",
		"content": [ { "type": "text", "text": "ok" } ],
		"model": "claude-opus-4-5",
		"stop_reason": "end_turn",
		"usage": { "input_tokens": 1, "output_tokens": 1 }
	}
	""";

	private static HttpResponseMessage MakeResponse(HttpStatusCode status, string body)
		=> new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

	[Fact]
	public async Task SendAsync_Http503_RetriedUpToThreeTimes()
	{
		// First call: 503; retry succeeds. The pipeline must call twice (1 + 1 retry)
		// and return the second response's payload, with no Error.
		var handler = FakeHttpMessageHandler.ReturnsSequence(
			MakeResponse(HttpStatusCode.ServiceUnavailable, "{}"),
			MakeResponse(HttpStatusCode.OK, SuccessBody));
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("s", "u"));

		Assert.Equal(2, handler.CallCount);
		Assert.Null(resp.Error);
		Assert.Equal("ok", resp.Text);
	}

	[Fact]
	public async Task SendAsync_RetriesExhausted_ReturnsServerError()
	{
		// Both attempts return 503; pipeline gives up and surfaces server_error.
		var handler = FakeHttpMessageHandler.ReturnsSequence(
			MakeResponse(HttpStatusCode.ServiceUnavailable, "{}"),
			MakeResponse(HttpStatusCode.ServiceUnavailable, "{}"));
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("s", "u"));

		Assert.True(handler.CallCount >= 2);
		Assert.NotNull(resp.Error);
		Assert.Equal("server_error", resp.Error!.ErrorCode);
		Assert.Equal(503, resp.Error.HttpStatus);
	}

	[Fact]
	public async Task SendAsync_Http429_ReturnsRateLimit()
	{
		// 429 is NOT retried; pipeline normalises straight to rate_limit.
		var handler = FakeHttpMessageHandler.ReturnsJson((HttpStatusCode)429, "{}");
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("s", "u"));

		Assert.NotNull(resp.Error);
		Assert.Equal("rate_limit", resp.Error!.ErrorCode);
		Assert.Equal(429, resp.Error.HttpStatus);
	}
}
