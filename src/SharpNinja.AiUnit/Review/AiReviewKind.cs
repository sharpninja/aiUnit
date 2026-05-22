namespace SharpNinja.AiUnit.Review;

/// <summary>Built-in aiUnit review categories.</summary>
public enum AiReviewKind
{
	/// <summary>Review source code and tests for defects, regressions, and maintainability issues.</summary>
	Code,

	/// <summary>Review an implementation plan for completeness, sequencing, and risk.</summary>
	Plan,

	/// <summary>Review the project state across requirements, implementation, tests, and operational readiness.</summary>
	Project,
}
