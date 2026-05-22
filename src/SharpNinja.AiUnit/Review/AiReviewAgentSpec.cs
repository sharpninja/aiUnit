namespace SharpNinja.AiUnit.Review;

/// <summary>Agent strategy details supplied by a review attribute.</summary>
public sealed record AiReviewAgentSpec(
	string? Name = null,
	string? Kind = null,
	string? BaseUrl = null,
	string? Model = null,
	string? ApiKeyEnvVar = null,
	string? Command = null,
	int? TimeoutSeconds = null,
	double? Temperature = null)
{
	/// <summary>True when this spec contains inline strategy details beyond a named strategy.</summary>
	public bool HasInlineDetails =>
		!string.IsNullOrWhiteSpace(Kind)
		|| !string.IsNullOrWhiteSpace(BaseUrl)
		|| !string.IsNullOrWhiteSpace(Model)
		|| !string.IsNullOrWhiteSpace(ApiKeyEnvVar)
		|| !string.IsNullOrWhiteSpace(Command)
		|| TimeoutSeconds is > 0
		|| Temperature.HasValue;
}
