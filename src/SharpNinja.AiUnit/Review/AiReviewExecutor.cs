using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Review;

internal static class AiReviewExecutor
{
	public static Task<string> ExecuteAsync(
		AiReviewExecutionRequest request,
		IAiReviewClientResolver resolver,
		CancellationToken cancellationToken = default)
		=> ExecuteAsync(request, resolver, sink: null, startedUtc: null, cancellationToken);

	/// <summary>
	/// Runs the review, persists a run-log entry via <paramref name="sink"/>
	/// (a file sink built from config when null), and injects the resulting
	/// run-log reference into the review JSON on every output path.
	/// </summary>
	public static async Task<string> ExecuteAsync(
		AiReviewExecutionRequest request,
		IAiReviewClientResolver resolver,
		IAiReviewRunLogSink? sink,
		DateTimeOffset? startedUtc = null,
		CancellationToken cancellationToken = default)
	{
		var started = startedUtc ?? DateTimeOffset.UtcNow;
		var production = await ProduceAsync(request, resolver, cancellationToken).ConfigureAwait(false);

		var entry = new AiReviewRunLogEntry(
			started,
			AiReviewPrompts.ReviewTypeName(request.ReviewKind),
			production.Agents,
			request.Prompt,
			production.Provider,
			production.Model,
			production.LatencyMs,
			production.TokenUsage,
			production.Error,
			production.FindingsJson);

		AiReviewRunLogRef? runLog = null;
		try
		{
			var effectiveSink = sink ?? FileAiReviewRunLogSink.FromConfig();
			runLog = effectiveSink.Write(entry);
		}
		catch
		{
			// Run-log persistence is best-effort: a logging failure must never
			// fail the review itself. The review JSON is returned without a
			// runLog reference in that case.
		}

		return runLog is null
			? production.FindingsJson
			: AiReviewJson.InjectRunLog(production.FindingsJson, runLog);
	}

	private static async Task<AiReviewProduction> ProduceAsync(
		AiReviewExecutionRequest request,
		IAiReviewClientResolver resolver,
		CancellationToken cancellationToken)
	{
		if (request.Agents.Count == 0)
		{
			var resolved = resolver.ResolveDefault();
			return await ExecuteSingleAsync(request, resolved, cancellationToken).ConfigureAwait(false);
		}

		var individual = new List<AiReviewAgentResult>(request.Agents.Count);
		var names = new List<string>(request.Agents.Count);
		AiReviewProduction? lastSingle = null;
		foreach (var spec in request.Agents)
		{
			var resolved = resolver.Resolve(spec);
			var single = await ExecuteSingleAsync(request, resolved, cancellationToken).ConfigureAwait(false);
			individual.Add(new AiReviewAgentResult(resolved.Name, single.FindingsJson));
			names.Add(resolved.Name);
			lastSingle = single;
		}

		if (individual.Count == 1)
		{
			return lastSingle!;
		}

		var aggregator = resolver.ResolveDefault();
		if (aggregator.Client is null)
		{
			var error = string.IsNullOrWhiteSpace(aggregator.SkipReason)
				? "Multiple review agents ran, but the default agent was unavailable for aggregation."
				: aggregator.SkipReason;
			var json = AiReviewJson.NormalizeOrWrap(
				request.ReviewKind,
				aggregator.Name,
				text: null,
				error: error,
				agentReviews: individual);
			return new AiReviewProduction(json, names, null, null, 0, FrontierTokenUsage.Zero, error);
		}

		var aggregateRequest = BuildFrontierRequest(
			request,
			AiReviewPrompts.BuildAggregationPrompt(request.ReviewKind, request.Prompt, individual));
		var response = await aggregator.Client.SendAsync(aggregateRequest, cancellationToken).ConfigureAwait(false);
		var aggregateJson = NormalizeResponse(request.ReviewKind, aggregator.Name, response, individual);
		names.Add(aggregator.Name);
		return new AiReviewProduction(
			aggregateJson,
			names,
			response.Provider,
			response.ModelVersion,
			response.LatencyMs,
			response.TokenUsage,
			response.Error?.Message);
	}

	private static async Task<AiReviewProduction> ExecuteSingleAsync(
		AiReviewExecutionRequest request,
		AiReviewResolvedClient resolved,
		CancellationToken cancellationToken)
	{
		if (resolved.Client is null)
		{
			var error = string.IsNullOrWhiteSpace(resolved.SkipReason)
				? $"Review agent '{resolved.Name}' did not resolve."
				: resolved.SkipReason;
			var json = AiReviewJson.NormalizeOrWrap(request.ReviewKind, resolved.Name, text: null, error: error);
			return new AiReviewProduction(json, new[] { resolved.Name }, null, null, 0, FrontierTokenUsage.Zero, error);
		}

		var frontierRequest = BuildFrontierRequest(
			request,
			AiReviewPrompts.BuildUserPrompt(request.ReviewKind, request.Prompt));
		var response = await resolved.Client.SendAsync(frontierRequest, cancellationToken).ConfigureAwait(false);
		var resultJson = NormalizeResponse(request.ReviewKind, resolved.Name, response, agentReviews: null);
		return new AiReviewProduction(
			resultJson,
			new[] { resolved.Name },
			response.Provider,
			response.ModelVersion,
			response.LatencyMs,
			response.TokenUsage,
			response.Error?.Message);
	}

	private static FrontierRequest BuildFrontierRequest(AiReviewExecutionRequest request, string userPrompt) =>
		new(
			SystemPrompt: AiReviewPrompts.BuildSystemPrompt(request.ReviewKind),
			UserMessage: userPrompt,
			Tools:
			[
				new FrontierTool(
					"report_review_findings",
					"Report aiUnit review findings as JSON.",
					AiReviewFindingsSchema.JsonSchema)
			],
			MaxTokens: request.MaxTokens is > 0 ? request.MaxTokens : null,
			Temperature: 0,
			RequireJsonOutput: true);

	private static string NormalizeResponse(
		AiReviewKind kind,
		string agent,
		FrontierResponse response,
		IReadOnlyList<AiReviewAgentResult>? agentReviews)
	{
		if (response.Error is not null)
		{
			return AiReviewJson.NormalizeOrWrap(
				kind,
				agent,
				response.Text,
				response.Error.Message,
				response.Provider,
				response.ModelVersion,
				agentReviews);
		}

		return AiReviewJson.NormalizeOrWrap(
			kind,
			agent,
			response.Text,
			provider: response.Provider,
			model: response.ModelVersion,
			agentReviews: agentReviews);
	}

	/// <summary>Internal carrier of the findings JSON plus run-log metadata.</summary>
	private sealed record AiReviewProduction(
		string FindingsJson,
		IReadOnlyList<string> Agents,
		string? Provider,
		string? Model,
		long LatencyMs,
		FrontierTokenUsage TokenUsage,
		string? Error);
}
