using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Phase 1 foundation tests. Verify the ported Frontier* records (Request,
/// Response, Attachment, TokenUsage, Error, Tool) serialize round-trip via
/// System.Text.Json AND that the SharpNinja.AiUnit.Frontier namespace is
/// reachable from a consumer assembly (this test project IS that consumer).
/// </summary>
public class FrontierContractsTests
{
	[Fact]
	public void Contracts_RoundTripJson()
	{
		// Build a Request that exercises every optional field
		var bytes = Encoding.UTF8.GetBytes("attachment-content");
		var attachment = new FrontierAttachment("text/plain", "notes.txt", bytes);
		var tool = new FrontierTool("get_weather", "fetch weather", "{\"type\":\"object\"}");

		var request = new FrontierRequest(
			SystemPrompt: "you are helpful",
			UserMessage: "hello",
			Tools: new List<FrontierTool> { tool },
			Attachments: new List<FrontierAttachment> { attachment },
			MaxTokens: 256,
			Temperature: 0.4,
			RequireJsonOutput: true);

		var reqJson = JsonSerializer.Serialize(request);
		var decoded = JsonSerializer.Deserialize<FrontierRequest>(reqJson)!;

		Assert.Equal(request.SystemPrompt, decoded.SystemPrompt);
		Assert.Equal(request.UserMessage, decoded.UserMessage);
		Assert.Equal(request.MaxTokens, decoded.MaxTokens);
		Assert.Equal(request.Temperature, decoded.Temperature);
		Assert.Equal(request.RequireJsonOutput, decoded.RequireJsonOutput);
		Assert.NotNull(decoded.Tools);
		Assert.Single(decoded.Tools!);
		Assert.Equal("get_weather", decoded.Tools![0].Name);
		Assert.NotNull(decoded.Attachments);
		Assert.Single(decoded.Attachments!);
		Assert.Equal("text/plain", decoded.Attachments![0].MediaType);

		// Response with TokenUsage and no error
		var usage = new FrontierTokenUsage(InputTokens: 12, OutputTokens: 7, TotalTokens: 19);
		var response = new FrontierResponse(
			Text: "{\"ok\":true}",
			TokenUsage: usage,
			LatencyMs: 123,
			Provider: "anthropic",
			ModelVersion: "claude-x",
			EstimatedCostUsd: 0.0123m,
			Error: null);
		var respJson = JsonSerializer.Serialize(response);
		var decodedResp = JsonSerializer.Deserialize<FrontierResponse>(respJson)!;
		Assert.Equal(response.Text, decodedResp.Text);
		Assert.Equal(response.TokenUsage, decodedResp.TokenUsage);
		Assert.Equal(response.Provider, decodedResp.Provider);
		Assert.Equal(response.ModelVersion, decodedResp.ModelVersion);
		Assert.Equal(response.EstimatedCostUsd, decodedResp.EstimatedCostUsd);
		Assert.Null(decodedResp.Error);

		// Error record round-trip
		var err = new FrontierError("auth", "401 unauthorized", 401);
		var errJson = JsonSerializer.Serialize(err);
		var decodedErr = JsonSerializer.Deserialize<FrontierError>(errJson)!;
		Assert.Equal("auth", decodedErr.ErrorCode);
		Assert.Equal(401, decodedErr.HttpStatus);

		// Zero usage singleton
		Assert.Equal(0, FrontierTokenUsage.Zero.InputTokens);
		Assert.Equal(0, FrontierTokenUsage.Zero.OutputTokens);
		Assert.Equal(0, FrontierTokenUsage.Zero.TotalTokens);

		// MaxSizeBytes constant exists
		Assert.True(FrontierAttachment.MaxSizeBytes > 0);
		Assert.True(attachment.IsImage == false);
	}

	[Fact]
	public void Contracts_NamespacesResolve()
	{
		// Compile-time + runtime proof that every public type is reachable from
		// the SharpNinja.AiUnit.Frontier namespace alone (no TruckMate fallback).
		var ns = typeof(FrontierRequest).Namespace;
		Assert.Equal("SharpNinja.AiUnit.Frontier", ns);
		Assert.Equal(ns, typeof(FrontierResponse).Namespace);
		Assert.Equal(ns, typeof(FrontierAttachment).Namespace);
		Assert.Equal(ns, typeof(FrontierTokenUsage).Namespace);
		Assert.Equal(ns, typeof(FrontierError).Namespace);
		Assert.Equal(ns, typeof(FrontierTool).Namespace);
		Assert.Equal(ns, typeof(IFrontierModelClient).Namespace);
		Assert.Equal(ns, typeof(IFrontierProviderConfig).Namespace);
		Assert.Equal(ns, typeof(FrontierProviderConfig).Namespace);

		// Image attachment detection
		var imgAtt = new FrontierAttachment("image/png", "shot.png", new byte[] { 1, 2, 3 });
		Assert.True(imgAtt.IsImage);

		// Plain config record
		var cfg = new FrontierProviderConfig(
			ApiKey: "secret",
			ModelVersion: "model-x",
			BaseUrl: new Uri("https://api.example.com"),
			Timeout: TimeSpan.FromSeconds(15));
		Assert.Equal("secret", cfg.ApiKey);
		Assert.Equal("model-x", cfg.ModelVersion);
		Assert.Equal(new Uri("https://api.example.com"), cfg.BaseUrl);
		Assert.Equal(TimeSpan.FromSeconds(15), cfg.Timeout);
	}
}
