using SharpNinja.AiUnit.Resilience;

namespace SharpNinja.AiUnit.Tests.Resilience;

public class ResilienceOptionsTests
{
    [Fact]
    public void LibraryDefault_HasExpectedValues()
    {
        var d = ResilienceOptions.LibraryDefault;

        Assert.True(d.ResilienceEnabled);
        Assert.Equal(180, d.TimeoutSeconds);
        Assert.Equal(1, d.MaxRetries);
        Assert.Equal(2000, d.RetryBaseDelayMs);
        Assert.Equal("exponential", d.RetryBackoff, ignoreCase: true);
        Assert.Equal(5, d.BreakAfterConsecutiveFailures);
        Assert.Equal(30, d.BreakDurationSeconds);
        Assert.Null(d.FallbackStrategy);
    }

    [Fact]
    public void WithExpression_OverridesSelectedFields()
    {
        var opts = ResilienceOptions.LibraryDefault with { TimeoutSeconds = 42, MaxRetries = 3 };

        Assert.Equal(42, opts.TimeoutSeconds);
        Assert.Equal(3, opts.MaxRetries);
        Assert.Equal(ResilienceOptions.LibraryDefault.RetryBaseDelayMs, opts.RetryBaseDelayMs);
        Assert.Equal(ResilienceOptions.LibraryDefault.BreakAfterConsecutiveFailures,
            opts.BreakAfterConsecutiveFailures);
    }

    [Fact]
    public void ResilienceEnabled_False_DisablesPipeline()
    {
        var opts = ResilienceOptions.LibraryDefault with { ResilienceEnabled = false };

        Assert.False(opts.ResilienceEnabled);
        Assert.Equal(ResilienceOptions.LibraryDefault.TimeoutSeconds, opts.TimeoutSeconds);
    }
}
