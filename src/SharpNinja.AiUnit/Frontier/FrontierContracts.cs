using System;
using System.Collections.Generic;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Reserved-for-v2 tool descriptor. Iteration 1 adapters ignore Tools - the
/// first aiUnit use case is structured intent extraction (single JSON-mode
/// call), not tool calling.
/// </summary>
public sealed record FrontierTool(string Name, string Description, string JsonSchema);

/// <summary>
/// Multi-modal attachment for vision-enabled frontier requests. Image
/// attachments use a media-type starting with <c>image/</c>; adapters encode
/// them as the provider's native vision content block (Anthropic
/// <c>image</c> block, OpenAI / xAI <c>image_url</c> data URI, Gemini
/// <c>inlineData</c> part). Anything else is treated as text and inlined
/// ahead of the user prompt as a fenced code block.
///
/// Attachments larger than <see cref="FrontierAttachment.MaxSizeBytes"/>
/// are rejected client-side with
/// <see cref="FrontierError"/>(<c>ErrorCode = "AttachmentTooLarge"</c>) so
/// adapters never ship oversize payloads.
/// </summary>
public sealed record FrontierAttachment(
	string MediaType,
	string Name,
	byte[] Data)
{
	/// <summary>5 MB ceiling for any single attachment.</summary>
	public const int MaxSizeBytes = 5 * 1024 * 1024;

	/// <summary>True when <see cref="MediaType"/> starts with <c>image/</c>.</summary>
	public bool IsImage =>
		!string.IsNullOrEmpty(MediaType)
		&& MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Frontier model request payload shared across providers. Iteration 1
/// implementations use only SystemPrompt + UserMessage + RequireJsonOutput
/// + (optional) MaxTokens / Temperature. Tools is reserved for v2.
/// Iteration 5 adds optional <see cref="Attachments"/> for vision-enabled
/// calls (image content + auxiliary text inlined as fenced code).
/// </summary>
public sealed record FrontierRequest(
	string SystemPrompt,
	string UserMessage,
	IReadOnlyList<FrontierTool>? Tools = null,
	IReadOnlyList<FrontierAttachment>? Attachments = null,
	int? MaxTokens = null,
	double? Temperature = null,
	bool RequireJsonOutput = false);

/// <summary>
/// Common shape returned by every adapter. <see cref="Text"/> is non-null on
/// success; <see cref="Error"/> is non-null on every failure path (auth,
/// rate-limit, network, timeout, malformed JSON, unexpected exception).
/// Adapters never throw to the caller (except
/// <see cref="OperationCanceledException"/> when the supplied token is
/// cancelled by the caller).
/// </summary>
public sealed record FrontierResponse(
	string? Text,
	FrontierTokenUsage TokenUsage,
	long LatencyMs,
	string Provider,
	string ModelVersion,
	decimal? EstimatedCostUsd,
	FrontierError? Error);

/// <summary>Per-call token accounting reported by the provider (or zero if unknown).</summary>
public sealed record FrontierTokenUsage(int InputTokens, int OutputTokens, int TotalTokens)
{
	/// <summary>Reusable zero-value instance.</summary>
	public static FrontierTokenUsage Zero { get; } = new(0, 0, 0);
}

/// <summary>
/// Captures a failure mode without leaking secrets. ErrorCode values used by
/// adapters: "auth", "rate_limit", "server_error", "network", "timeout",
/// "malformed_response", "unexpected", "AttachmentTooLarge", "cli_error",
/// "empty_response".
/// </summary>
public sealed record FrontierError(string ErrorCode, string Message, int? HttpStatus);
