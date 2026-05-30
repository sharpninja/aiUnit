using System;
using System.Collections.Generic;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Review;

/// <summary>
/// Reference to a persisted review run log. Embedded into every review result
/// JSON under the <c>runLog</c> property so consumers can open the full run
/// transcript locally (<see cref="Path"/>) or online (<see cref="Url"/>).
/// </summary>
/// <param name="Path">Local filesystem path to the run-log result file.</param>
/// <param name="Url">Optional online URL to the run log (when a base URL is configured).</param>
/// <param name="StartedUtc">UTC start time of the review run.</param>
public sealed record AiReviewRunLogRef(string Path, string? Url, DateTimeOffset StartedUtc);

/// <summary>
/// Captured record of a single review run, serialized by an
/// <see cref="IAiReviewRunLogSink"/> into the results directory.
/// </summary>
internal sealed record AiReviewRunLogEntry(
	DateTimeOffset StartedUtc,
	string ReviewType,
	IReadOnlyList<string> Agents,
	string Prompt,
	string? Provider,
	string? Model,
	long LatencyMs,
	FrontierTokenUsage TokenUsage,
	string? Error,
	string FindingsJson);
