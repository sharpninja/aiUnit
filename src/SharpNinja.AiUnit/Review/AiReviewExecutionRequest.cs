namespace SharpNinja.AiUnit.Review;

/// <summary>Runtime request created from a review attribute.</summary>
public sealed record AiReviewExecutionRequest(
	AiReviewKind ReviewKind,
	string Prompt,
	IReadOnlyList<AiReviewAgentSpec> Agents,
	int? MaxTokens = null);

internal sealed record AiReviewResolvedClient(
	string Name,
	SharpNinja.AiUnit.Frontier.IFrontierModelClient? Client,
	string? SkipReason);

internal sealed record AiReviewAgentResult(
	string Agent,
	string ResultJson);

internal interface IAiReviewClientResolver
{
	AiReviewResolvedClient ResolveDefault();

	AiReviewResolvedClient Resolve(AiReviewAgentSpec spec);
}
