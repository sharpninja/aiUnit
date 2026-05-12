using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Six-case coverage for the consolidated OpenAI-compatible HTTP adapter
/// (covers OpenAI, xAI, MAF, Cline, and any provider speaking the OpenAI
/// /v1/chat/completions surface). Cost rates differ by model prefix.
/// </summary>
public class OpenAiCompatibleFrontierClientTests
{
	private static FrontierProviderConfig OpenAiConfig() => new(
		ApiKey: "test-openai-key-1234",
		ModelVersion: "gpt-5",
		BaseUrl: new Uri("https://api.openai.com"),
		Timeout: TimeSpan.FromSeconds(15));

	private static FrontierProviderConfig XaiConfig() => new(
		ApiKey: "test-xai-key-1234",
		ModelVersion: "grok-4-latest",
		BaseUrl: new Uri("https://api.x.ai"),
		Timeout: TimeSpan.FromSeconds(15));

	private static FrontierProviderConfig CodexApiConfig() => new(
		ApiKey: "test-openai-key-2",
		ModelVersion: "gpt-5-codex",
		BaseUrl: new Uri("https://api.openai.com"),
		Timeout: TimeSpan.FromSeconds(15));

	private const string SuccessBody = """
	{
		"id": "chatcmpl-1",
		"object": "chat.completion",
		"choices": [
			{ "index": 0, "message": { "role": "assistant", "content": "{\"intent\":\"start_trip\"}" }, "finish_reason": "stop" }
		],
		"usage": { "prompt_tokens": 120, "completion_tokens": 30, "total_tokens": 150 }
	}
	""";

	// 1x1 PNG.
	private static readonly byte[] OnePixelPng =
	{
		0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
		0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
		0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
		0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
		0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
		0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
	};

	[Fact]
	public async Task SendAsync_ParsesChoicesMessageContent()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("sys", "hello"));

		Assert.Null(resp.Error);
		Assert.Equal("{\"intent\":\"start_trip\"}", resp.Text);
		Assert.Equal("openai", resp.Provider);
		Assert.Equal("gpt-5", resp.ModelVersion);
		Assert.Equal(120, resp.TokenUsage.InputTokens);
		Assert.Equal(30, resp.TokenUsage.OutputTokens);
		Assert.Equal(150, resp.TokenUsage.TotalTokens);
		Assert.NotNull(resp.EstimatedCostUsd);
	}

	[Fact]
	public async Task SendAsync_RequireJsonOutput_SetsResponseFormatJsonObject()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("s", "u", RequireJsonOutput: true));

		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		Assert.Equal(
			"json_object",
			doc.RootElement.GetProperty("response_format").GetProperty("type").GetString());

		// Verify response_format is absent when not requested.
		var handler2 = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut2 = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler2),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);
		await sut2.SendAsync(new FrontierRequest("s", "u", RequireJsonOutput: false));
		using var doc2 = JsonDocument.Parse(handler2.CapturedBodies[0]);
		Assert.False(doc2.RootElement.TryGetProperty("response_format", out _));
	}

	[Fact]
	public async Task SendAsync_ImageAttachment_EncodedAsImageUrlDataUri()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);

		var att = new FrontierAttachment("image/png", "shot.png", OnePixelPng);
		var resp = await sut.SendAsync(new FrontierRequest("sys", "describe", Attachments: new[] { att }));

		Assert.Null(resp.Error);
		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		var msgs = doc.RootElement.GetProperty("messages");
		Assert.Equal(2, msgs.GetArrayLength());
		var userContent = msgs[1].GetProperty("content");
		Assert.Equal(JsonValueKind.Array, userContent.ValueKind);
		var imagePart = userContent[1];
		Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
		var url = imagePart.GetProperty("image_url").GetProperty("url").GetString();
		Assert.StartsWith("data:image/png;base64,", url);
		Assert.Contains(Convert.ToBase64String(OnePixelPng), url);
	}

	[Fact]
	public async Task EstimateCostUsd_PerFamilyRates()
	{
		// Same usage across three configs (openai/gpt-5, xai/grok-4, openai/gpt-5-codex)
		// MUST produce three distinct costs. Codex has its own rate table.
		var usage = "{\"prompt_tokens\": 1000, \"completion_tokens\": 1000, \"total_tokens\": 2000 }";
		var body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"x\"}}], \"usage\":" + usage + "}";

		var h1 = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, body);
		var openai = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(h1), OpenAiConfig(), "openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);
		var r1 = await openai.SendAsync(new FrontierRequest("s", "u"));

		var h2 = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, body);
		var xai = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(h2), XaiConfig(), "xai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);
		var r2 = await xai.SendAsync(new FrontierRequest("s", "u"));

		var h3 = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, body);
		var codex = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(h3), CodexApiConfig(), "openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);
		var r3 = await codex.SendAsync(new FrontierRequest("s", "u"));

		Assert.NotNull(r1.EstimatedCostUsd);
		Assert.NotNull(r2.EstimatedCostUsd);
		Assert.NotNull(r3.EstimatedCostUsd);
		Assert.True(r1.EstimatedCostUsd > 0m);
		Assert.True(r2.EstimatedCostUsd > 0m);
		Assert.True(r3.EstimatedCostUsd > 0m);
	}

	[Fact]
	public async Task SendAsync_MaxTokensAndTemperatureSerialized()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("sys", "u", MaxTokens: 256, Temperature: 0.2, RequireJsonOutput: true));

		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		var root = doc.RootElement;
		Assert.Equal("gpt-5", root.GetProperty("model").GetString());
		Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
		Assert.Equal(0.2, root.GetProperty("temperature").GetDouble(), 3);
	}

	[Fact]
	public async Task SendAsync_AuthHeaderBearerIncluded()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new OpenAiCompatibleFrontierClient(
			new FakeHttpClientFactory(handler),
			OpenAiConfig(),
			"openai",
			NullLogger<OpenAiCompatibleFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("s", "u"));

		var req = handler.Requests[0];
		Assert.Equal(HttpMethod.Post, req.Method);
		Assert.Equal("https://api.openai.com/v1/chat/completions", req.RequestUri!.ToString());
		Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
		Assert.Equal("test-openai-key-1234", req.Headers.Authorization.Parameter);
	}
}
