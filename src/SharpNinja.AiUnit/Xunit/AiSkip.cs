namespace SharpNinja.AiUnit.Xunit;

/// <summary>
/// Convenience helpers that perform an xUnit v3 dynamic skip with
/// aiUnit-specific semantics. Use these from inside a test body when the
/// row/case needs to skip based on a runtime condition that the
/// discovery-time <see cref="AiFactAttribute"/> / <see cref="AiTheoryAttribute"/>
/// cannot observe (e.g. an env var the test itself just set, or a per-row
/// MemberData parameter).
/// </summary>
public static class AiSkip
{
	/// <summary>
	/// Skips the current test iff <see cref="AiStrategyFixture.Default"/> did
	/// not resolve a frontier client. The skip reason defaults to the
	/// fixture's <see cref="AiStrategyFixture.SkipReason"/> but callers may
	/// override with a clearer test-specific message.
	/// </summary>
	/// <param name="reason">Optional override for the skip reason.</param>
	public static void IfNoStrategy(string? reason = null)
	{
		var fx = AiStrategyFixture.Default;
		if (!fx.IsResolved)
		{
			var msg = string.IsNullOrEmpty(reason) ? fx.SkipReason : reason!;
			if (string.IsNullOrEmpty(msg))
			{
				msg = "No aiUnit strategy resolved.";
			}

			// xUnit v3 dynamic skip: a test that throws an exception whose message
			// begins with the dynamic-skip token is reported as skipped (not failed).
			throw new global::System.Exception(global::Xunit.v3.DynamicSkipToken.Value + msg);
		}
	}
}
