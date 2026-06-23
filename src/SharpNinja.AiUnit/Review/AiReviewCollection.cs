namespace SharpNinja.AiUnit.Review;

/// <summary>
/// xUnit collection that disables parallelization for aiUnit review tests
/// (<c>[AiCodeReview]</c> / <c>[AiPlanReview]</c> / <c>[AiProjectReview]</c>).
/// Apply <c>[Collection(AiReviewCollection.Name)]</c> to review test classes so
/// the runner schedules them serially, in addition to the process-wide gate in
/// <see cref="AiReviewExecutor"/> that guarantees serialization at the agent
/// call regardless of collection configuration.
/// </summary>
[global::Xunit.CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AiReviewCollection
{
	/// <summary>The well-known collection name consumers reference.</summary>
	public const string Name = "aiUnit AI Reviews";
}
