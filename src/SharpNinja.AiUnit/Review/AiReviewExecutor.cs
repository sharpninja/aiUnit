using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Review;

internal static class AiReviewExecutor
{
	public static async Task<string> ExecuteAsync(
		AiReviewExecutionRequest request,
		IAiReviewClientResolver resolver,
		CancellationToken cancellationToken = default)
	{
		if (request.Agents.Count == 0)
		{
			var resolved = resolver.ResolveDefault();
			return await ExecuteSingleAsync(request, resolved, cancellationToken).ConfigureAwait(false);
		}

		var individual = new List<AiReviewAgentResult>(request.Agents.Count);
		foreach (var spec in request.Agents)
		{
			var resolved = resolver.Resolve(spec);
			var json = await ExecuteSingleAsync(request, resolved, cancellationToken).ConfigureAwait(false);
			individual.Add(new AiReviewAgentResult(resolved.Name, json));
		}

		if (individual.Count == 1)
		{
			return individual[0].ResultJson;
		}

		var aggregator = resolver.ResolveDefault();
		if (aggregator.Client is null)
		{
			return AiReviewJson.NormalizeOrWrap(
				request.ReviewKind,
				aggregator.Name,
				text: null,
				error: string.IsNullOrWhiteSpace(aggregator.SkipReason)
					? "Multiple review agents ran, but the default agent was unavailable for aggregation."
					: aggregator.SkipReason,
				agentReviews: individual);
		}

		var aggregateRequest = BuildFrontierRequest(
			request,
			AiReviewPrompts.BuildAggregationPrompt(request.ReviewKind, request.Prompt, individual));
		var response = await aggregator.Client.SendAsync(aggregateRequest, cancellationToken).ConfigureAwait(false);
		return NormalizeResponse(request.ReviewKind, aggregator.Name, response, individual);
	}

	private static async Task<string> ExecuteSingleAsync(
		AiReviewExecutionRequest request,
		AiReviewResolvedClient resolved,
		CancellationToken cancellationToken)
	{
		if (resolved.Client is null)
		{
			return AiReviewJson.NormalizeOrWrap(
				request.ReviewKind,
				resolved.Name,
				text: null,
				error: string.IsNullOrWhiteSpace(resolved.SkipReason)
					? $"Review agent '{resolved.Name}' did not resolve."
					: resolved.SkipReason);
		}

		var frontierRequest = BuildFrontierRequest(
			request,
			AiReviewPrompts.BuildUserPrompt(request.ReviewKind, request.Prompt));
		var response = await resolved.Client.SendAsync(frontierRequest, cancellationToken).ConfigureAwait(false);
		return NormalizeResponse(request.ReviewKind, resolved.Name, response, agentReviews: null);
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
}
