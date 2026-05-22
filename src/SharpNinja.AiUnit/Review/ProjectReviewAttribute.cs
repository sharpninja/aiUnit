namespace SharpNinja.AiUnit.Review;

/// <summary>Supplies an aiUnit project-review result JSON row to an xUnit theory method.</summary>
public sealed class ProjectReviewAttribute : AiReviewAttribute
{
	/// <summary>Creates a project-review row. Empty prompts use the default project-review YAML prompt.</summary>
	public ProjectReviewAttribute(string? prompt = null)
		: base(AiReviewKind.Project, prompt)
	{
	}
}
