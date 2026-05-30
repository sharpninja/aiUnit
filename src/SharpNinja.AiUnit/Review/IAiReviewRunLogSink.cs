namespace SharpNinja.AiUnit.Review;

/// <summary>
/// Persists a review run log and returns a reference that is embedded into the
/// review result JSON. The default implementation is
/// <see cref="FileAiReviewRunLogSink"/>; tests inject in-memory fakes.
/// </summary>
internal interface IAiReviewRunLogSink
{
	/// <summary>Writes the run-log entry and returns its reference.</summary>
	AiReviewRunLogRef Write(AiReviewRunLogEntry entry);
}
