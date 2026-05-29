namespace SharpNinja.AiUnit.Review;

/// <summary>Supplies an aiUnit code-review result JSON row to an xUnit theory method.</summary>
public sealed class AiCodeReviewAttribute : AiReviewAttribute
{
	/// <summary>Creates a code-review row. Empty prompts use the default code-review YAML prompt.</summary>
	public AiCodeReviewAttribute(string? prompt = null)
		: base(AiReviewKind.Code, prompt)
	{
	}
}
