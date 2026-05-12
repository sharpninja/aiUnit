namespace SharpNinja.AiUnit.Xunit;

/// <summary>
/// Drop-in replacement for <see cref="global::Xunit.TheoryAttribute"/> that
/// auto-skips at discovery time when no aiUnit strategy resolves. Per-row
/// skipping still works via <see cref="AiSkip.IfNoStrategy"/> or any other
/// <see cref="global::Xunit.Skip"/> helper used inside the test body.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
public sealed class AiTheoryAttribute : global::Xunit.SkippableTheoryAttribute
{
	/// <summary>
	/// Creates a new <see cref="AiTheoryAttribute"/>. When the shared
	/// <see cref="AiStrategyFixture.Default"/> reports a non-empty skip
	/// reason, that reason is forwarded to the base
	/// <see cref="global::Xunit.SkippableTheoryAttribute.Skip"/>.
	/// </summary>
	public AiTheoryAttribute()
	{
		var fx = AiStrategyFixture.Default;
		if (!fx.IsResolved && !string.IsNullOrEmpty(fx.SkipReason))
		{
			Skip = fx.SkipReason;
		}
	}
}
