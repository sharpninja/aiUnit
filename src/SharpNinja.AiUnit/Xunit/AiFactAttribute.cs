namespace SharpNinja.AiUnit.Xunit;

/// <summary>
/// Drop-in replacement for <see cref="global::Xunit.FactAttribute"/> that
/// auto-skips at discovery when <see cref="AiStrategyFixture.Default"/> has
/// no resolved frontier client. The skip reason is forwarded to xUnit via
/// the base <see cref="global::Xunit.SkippableFactAttribute.Skip"/> property
/// so the run output records WHY the test was skipped.
///
/// Limitation: the attribute ctor runs at xUnit DISCOVERY time, so the
/// fixture is evaluated once before any test body executes. Tests that need
/// to mutate env vars per-iteration and observe runtime resolution should
/// use a plain <see cref="global::Xunit.SkippableFactAttribute"/> plus
/// <see cref="AiSkip.IfNoStrategy"/> inside the test body instead.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class AiFactAttribute : global::Xunit.SkippableFactAttribute
{
	/// <summary>
	/// Creates a new <see cref="AiFactAttribute"/>. When the shared
	/// <see cref="AiStrategyFixture.Default"/> reports a non-empty
	/// <see cref="AiStrategyFixture.SkipReason"/>, that reason is forwarded
	/// to <see cref="global::Xunit.SkippableFactAttribute.Skip"/>.
	/// </summary>
	public AiFactAttribute()
	{
		var fx = AiStrategyFixture.Default;
		if (!fx.IsResolved && !string.IsNullOrEmpty(fx.SkipReason))
		{
			Skip = fx.SkipReason;
		}
	}
}
