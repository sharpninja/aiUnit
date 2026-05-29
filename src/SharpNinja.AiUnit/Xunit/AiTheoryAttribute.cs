using SharpNinja.AiUnit.Resilience;

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
	/// <summary>Per-attempt timeout in seconds. -1 = inherit from strategy or library default.</summary>
	public int TimeoutSeconds { get; set; } = -1;

	/// <summary>Maximum retry attempts on transient failure. -1 = inherit.</summary>
	public int MaxRetries { get; set; } = -1;

	/// <summary>Base retry delay in milliseconds. -1 = inherit.</summary>
	public int RetryBaseDelayMs { get; set; } = -1;

	/// <summary>Backoff type: "exponential", "linear", or "constant". Null = inherit.</summary>
	public string? RetryBackoff { get; set; }

	/// <summary>Open circuit after this many consecutive failures. -1 = inherit.</summary>
	public int BreakAfterConsecutiveFailures { get; set; } = -1;

	/// <summary>How long the circuit stays open in seconds. -1 = inherit.</summary>
	public int BreakDurationSeconds { get; set; } = -1;

	/// <summary>Strategy name to use as fallback. Null = inherit.</summary>
	public string? FallbackStrategy { get; set; }

	/// <summary>Enable or disable the resilience pipeline. Null = inherit.</summary>
	public bool? ResilienceEnabled { get; set; }

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

	/// <summary>
	/// Merges attribute-level sentinel values over <paramref name="baseOpts"/>,
	/// returning fully-resolved <see cref="ResilienceOptions"/> for this test.
	/// Sentinel values (-1 / null) inherit from <paramref name="baseOpts"/>.
	/// </summary>
	public ResilienceOptions GetResilienceOptions(ResilienceOptions baseOpts) =>
		baseOpts with
		{
			ResilienceEnabled = ResilienceEnabled ?? baseOpts.ResilienceEnabled,
			TimeoutSeconds = TimeoutSeconds >= 0 ? TimeoutSeconds : baseOpts.TimeoutSeconds,
			MaxRetries = MaxRetries >= 0 ? MaxRetries : baseOpts.MaxRetries,
			RetryBaseDelayMs = RetryBaseDelayMs >= 0 ? RetryBaseDelayMs : baseOpts.RetryBaseDelayMs,
			RetryBackoff = RetryBackoff ?? baseOpts.RetryBackoff,
			BreakAfterConsecutiveFailures = BreakAfterConsecutiveFailures >= 0
				? BreakAfterConsecutiveFailures
				: baseOpts.BreakAfterConsecutiveFailures,
			BreakDurationSeconds = BreakDurationSeconds >= 0
				? BreakDurationSeconds
				: baseOpts.BreakDurationSeconds,
			FallbackStrategy = FallbackStrategy ?? baseOpts.FallbackStrategy
		};
}
