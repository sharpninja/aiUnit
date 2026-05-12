using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Six-case coverage for the Anthropic Claude HTTP adapter (Phase 2):
/// content[].text parsing, refusal handling, base64 image source encoding,
/// oversize attachment rejection, x-api-key auth header, 401 -> auth.
/// </summary>
public class ClaudeFrontierClientTests
{
	private static FrontierProviderConfig Config(string key = "test-anthropic-key-1234")
		=> new(
			ApiKey: key,
			ModelVersion: "claude-opus-4-5",
			BaseUrl: new Uri("https://api.anthropic.com"),
			Timeout: TimeSpan.FromSeconds(15));

	private const string SuccessBody = """
	{
		"id": "msg_1",
		"type": "message",
		"role": "assistant",
		"content": [
			{ "type": "text", "text": "Hello " },
			{ "type": "text", "text": "world." }
		],
		"model": "claude-opus-4-5",
		"stop_reason": "end_turn",
		"usage": { "input_tokens": 80, "output_tokens": 20 }
	}
	""";

	private const string RefusalBody = """
	{
		"id": "msg_2",
		"type": "message",
		"role": "assistant",
		"content": [
			{ "type": "text", "text": "I cannot help with that." }
		],
		"model": "claude-opus-4-5",
		"stop_reason": "refusal",
		"usage": { "input_tokens": 50, "output_tokens": 8 }
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
	public async Task SendAsync_ParsesContentTextBlocks()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("sys", "u"));

		Assert.Null(resp.Error);
		Assert.Equal("Hello world.", resp.Text);
		Assert.Equal("anthropic", resp.Provider);
		Assert.Equal("claude-opus-4-5", resp.ModelVersion);
		Assert.Equal(80, resp.TokenUsage.InputTokens);
		Assert.Equal(20, resp.TokenUsage.OutputTokens);
		Assert.Equal(100, resp.TokenUsage.TotalTokens);
		Assert.NotNull(resp.EstimatedCostUsd);
	}

	[Fact]
	public async Task SendAsync_RefusalReturnsRefusalText()
	{
		// Refusal still parses as a successful response with the assistant's
		// refusal text in Text (Anthropic encodes refusals as a normal text
		// block plus stop_reason=refusal).
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, RefusalBody);
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("sys", "u"));

		Assert.Null(resp.Error);
		Assert.Equal("I cannot help with that.", resp.Text);
	}

	[Fact]
	public async Task SendAsync_ImageAttachment_EncodedAsBase64Source()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var att = new FrontierAttachment("image/png", "shot.png", OnePixelPng);
		var resp = await sut.SendAsync(new FrontierRequest("sys", "describe", Attachments: new[] { att }));

		Assert.Null(resp.Error);
		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");
		Assert.Equal(JsonValueKind.Array, content.ValueKind);
		var imageBlock = content[0];
		Assert.Equal("image", imageBlock.GetProperty("type").GetString());
		var src = imageBlock.GetProperty("source");
		Assert.Equal("base64", src.GetProperty("type").GetString());
		Assert.Equal("image/png", src.GetProperty("media_type").GetString());
		Assert.Equal(Convert.ToBase64String(OnePixelPng), src.GetProperty("data").GetString());
	}

	[Fact]
	public async Task SendAsync_OversizeAttachment_RejectedBeforeSend()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var oversize = new FrontierAttachment("image/png", "huge.png", new byte[FrontierAttachment.MaxSizeBytes + 1]);
		var resp = await sut.SendAsync(new FrontierRequest("sys", "u", Attachments: new[] { oversize }));

		Assert.NotNull(resp.Error);
		Assert.Equal("AttachmentTooLarge", resp.Error!.ErrorCode);
		Assert.Equal(0, handler.CallCount);
	}

	[Fact]
	public async Task SendAsync_AuthHeaderIncluded()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("sys", "u"));

		var req = handler.Requests[0];
		Assert.Equal(HttpMethod.Post, req.Method);
		Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
		Assert.True(req.Headers.Contains("x-api-key"));
		Assert.Equal("test-anthropic-key-1234", string.Join(",", req.Headers.GetValues("x-api-key")));
		Assert.True(req.Headers.Contains("anthropic-version"));
	}

	[Fact]
	public async Task SendAsync_Http401_ReturnsAuthError()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.Unauthorized, "{}");
		var sut = new ClaudeFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<ClaudeFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("s", "u"));

		Assert.NotNull(resp.Error);
		Assert.Equal("auth", resp.Error!.ErrorCode);
		Assert.Equal(401, resp.Error.HttpStatus);
	}
}
