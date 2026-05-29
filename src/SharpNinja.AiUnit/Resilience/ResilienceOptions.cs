namespace SharpNinja.AiUnit.Resilience;

/// <summary>
/// Fully-resolved resilience configuration for one pipeline instance.
/// All fields are non-nullable; sentinels live only on attribute properties.
/// </summary>
public sealed record ResilienceOptions(
    bool ResilienceEnabled,
    int TimeoutSeconds,
    int MaxRetries,
    int RetryBaseDelayMs,
    string RetryBackoff,
    int BreakAfterConsecutiveFailures,
    int BreakDurationSeconds,
    string? FallbackStrategy)
{
    /// <summary>Library defaults applied when no strategy or attribute override is present.</summary>
    public static ResilienceOptions LibraryDefault { get; } = new(
        ResilienceEnabled: true,
        TimeoutSeconds: 180,
        MaxRetries: 1,
        RetryBaseDelayMs: 2000,
        RetryBackoff: "exponential",
        BreakAfterConsecutiveFailures: 5,
        BreakDurationSeconds: 30,
        FallbackStrategy: null);
}
