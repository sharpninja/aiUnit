using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Four-case coverage for the Google Gemini adapter (Phase 2): candidates
/// parsing, systemInstruction encoding, inlineData image encoding, key= query
/// param auth.
/// </summary>
public class GeminiFrontierClientTests
{
	private static FrontierProviderConfig Config(string key = "test-google-key-1234")
		=> new(
			ApiKey: key,
			ModelVersion: "gemini-2.5-pro",
			BaseUrl: new Uri("https://generativelanguage.googleapis.com"),
			Timeout: TimeSpan.FromSeconds(15));

	private const string SuccessBody = """
	{
		"candidates": [
			{
				"content": {
					"role": "model",
					"parts": [ { "text": "{\"intent\":\"find_poi\"}" } ]
				},
				"finishReason": "STOP"
			}
		],
		"usageMetadata": {
			"promptTokenCount": 60,
			"candidatesTokenCount": 15,
			"totalTokenCount": 75
		}
	}
	""";

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
	public async Task SendAsync_ParsesCandidatesContentPartsText()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new GeminiFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<GeminiFrontierClient>.Instance);

		var resp = await sut.SendAsync(new FrontierRequest("sys", "u"));

		Assert.Null(resp.Error);
		Assert.Equal("{\"intent\":\"find_poi\"}", resp.Text);
		Assert.Equal("google", resp.Provider);
		Assert.Equal(60, resp.TokenUsage.InputTokens);
		Assert.Equal(15, resp.TokenUsage.OutputTokens);
		Assert.Equal(75, resp.TokenUsage.TotalTokens);
	}

	[Fact]
	public async Task SendAsync_SystemPrompt_EncodedAsSystemInstruction()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new GeminiFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<GeminiFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("be helpful", "find a thing", MaxTokens: 100, Temperature: 0.5, RequireJsonOutput: true));

		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		var root = doc.RootElement;
		var sys = root.GetProperty("systemInstruction");
		Assert.Equal("be helpful", sys.GetProperty("parts")[0].GetProperty("text").GetString());

		var gen = root.GetProperty("generationConfig");
		Assert.Equal("application/json", gen.GetProperty("responseMimeType").GetString());
		Assert.Equal(100, gen.GetProperty("maxOutputTokens").GetInt32());
		Assert.Equal(0.5, gen.GetProperty("temperature").GetDouble(), 3);
	}

	[Fact]
	public async Task SendAsync_ImageAttachment_EncodedAsInlineData()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new GeminiFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<GeminiFrontierClient>.Instance);

		var att = new FrontierAttachment("image/png", "shot.png", OnePixelPng);
		var resp = await sut.SendAsync(new FrontierRequest("sys", "describe", Attachments: new[] { att }));

		Assert.Null(resp.Error);
		using var doc = JsonDocument.Parse(handler.CapturedBodies[0]);
		var parts = doc.RootElement.GetProperty("contents")[0].GetProperty("parts");
		Assert.True(parts.GetArrayLength() >= 2);
		var imagePart = parts[0];
		var inline = imagePart.GetProperty("inlineData");
		Assert.Equal("image/png", inline.GetProperty("mimeType").GetString());
		Assert.Equal(Convert.ToBase64String(OnePixelPng), inline.GetProperty("data").GetString());
	}

	[Fact]
	public async Task SendAsync_AuthAsKeyQueryParam()
	{
		var handler = FakeHttpMessageHandler.ReturnsJson(HttpStatusCode.OK, SuccessBody);
		var sut = new GeminiFrontierClient(new FakeHttpClientFactory(handler), Config(), NullLogger<GeminiFrontierClient>.Instance);

		await sut.SendAsync(new FrontierRequest("sys", "u"));

		var req = handler.Requests[0];
		Assert.Equal(HttpMethod.Post, req.Method);
		var url = req.RequestUri!.ToString();
		Assert.Contains("/v1beta/models/gemini-2.5-pro:generateContent", url);
		Assert.Contains("key=test-google-key-1234", url);
		Assert.False(req.Headers.Contains("Authorization"));
		Assert.False(req.Headers.Contains("x-api-key"));
	}
}
