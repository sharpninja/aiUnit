namespace SharpNinja.AiUnit.Review;

/// <summary>Supplies an aiUnit plan-review result JSON row to an xUnit theory method.</summary>
public sealed class PlanReviewAttribute : AiReviewAttribute
{
	/// <summary>Creates a plan-review row. Empty prompts use the default plan-review YAML prompt.</summary>
	public PlanReviewAttribute(string? prompt = null)
		: base(AiReviewKind.Plan, prompt)
	{
	}
}
