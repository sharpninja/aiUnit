using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Resilience;

namespace SharpNinja.AiUnit.Tests.Resilience;

public class ResilientFrontierClientTests
{
    private static readonly FrontierRequest Req = new("sys", "user");

    [Fact]
    public async Task TimeoutFires_ReturnsTimeoutError()
    {
        var inner = StubFrontierClient.DelaysThenSucceeds(TimeSpan.FromSeconds(10));
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 1,
            MaxRetries = 0,
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal("timeout", resp.Error!.ErrorCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task TransientError_RetriedToSuccess()
    {
        var inner = StubFrontierClient.ReturnsSequence(
            StubFrontierClient.FailureResponse("server_error", "transient"),
            StubFrontierClient.FailureResponse("server_error", "transient"),
            StubFrontierClient.SuccessResponse("got it"));
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 2,
            RetryBaseDelayMs = 10,
            RetryBackoff = "constant",
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.Null(resp.Error);
        Assert.Equal("got it", resp.Text);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task AuthError_NotRetried()
    {
        var inner = StubFrontierClient.AlwaysFails("auth", "unauthorized");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 3,
            RetryBaseDelayMs = 10,
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal("auth", resp.Error!.ErrorCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task RateLimitError_NotRetried()
    {
        var inner = StubFrontierClient.AlwaysFails("rate_limit", "too many requests");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 3,
            RetryBaseDelayMs = 10,
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal("rate_limit", resp.Error!.ErrorCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterConsecutiveFailures()
    {
        const int breakAfter = 3;
        var inner = StubFrontierClient.AlwaysFails("server_error", "always failing");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 0,
            RetryBaseDelayMs = 10,
            BreakAfterConsecutiveFailures = breakAfter,
            BreakDurationSeconds = 300
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        for (var i = 0; i < breakAfter; i++)
            await sut.SendAsync(Req);

        var callsBefore = inner.CallCount;
        var resp = await sut.SendAsync(Req);

        Assert.Equal(callsBefore, inner.CallCount);
        Assert.NotNull(resp.Error);
        Assert.Equal("circuit_open", resp.Error!.ErrorCode);
    }

    [Fact]
    public async Task Fallback_InvokedWhenCircuitOpen()
    {
        const int breakAfter = 2;
        var inner = StubFrontierClient.AlwaysFails("server_error", "always failing");
        var fallback = StubFrontierClient.AlwaysSucceeds("fallback response");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 0,
            RetryBaseDelayMs = 10,
            BreakAfterConsecutiveFailures = breakAfter,
            BreakDurationSeconds = 300,
            FallbackStrategy = "fallback"
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance, fallback);

        for (var i = 0; i < breakAfter; i++)
            await sut.SendAsync(Req);

        var resp = await sut.SendAsync(Req);

        Assert.Null(resp.Error);
        Assert.Equal("fallback response", resp.Text);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task ResilienceDisabled_BypassesPipeline_NoRetry()
    {
        var inner = StubFrontierClient.AlwaysFails("server_error", "transient");
        var opts = ResilienceOptions.LibraryDefault with
        {
            ResilienceEnabled = false,
            MaxRetries = 3
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal("server_error", resp.Error!.ErrorCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task RetriesExhausted_ReturnsLastError()
    {
        var inner = StubFrontierClient.AlwaysFails("server_error", "always fails");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 2,
            RetryBaseDelayMs = 10,
            RetryBackoff = "constant",
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal("server_error", resp.Error!.ErrorCode);
        Assert.Equal(3, inner.CallCount); // initial + 2 retries
    }

    [Theory]
    [InlineData("spawn_failed")]
    [InlineData("AttachmentTooLarge")]
    public async Task OtherNonTransientErrors_NotRetried(string errorCode)
    {
        var inner = StubFrontierClient.AlwaysFails(errorCode, "non-transient");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 3,
            RetryBaseDelayMs = 10,
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.NotNull(resp.Error);
        Assert.Equal(errorCode, resp.Error!.ErrorCode);
        Assert.Equal(1, inner.CallCount);
    }

    [Theory]
    [InlineData("empty_response")]
    [InlineData("malformed_response")]
    [InlineData("network")]
    public async Task TransientErrorCodes_AreRetried(string errorCode)
    {
        var inner = StubFrontierClient.ReturnsSequence(
            StubFrontierClient.FailureResponse(errorCode, "transient"),
            StubFrontierClient.SuccessResponse("recovered"));
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 1,
            RetryBaseDelayMs = 10,
            RetryBackoff = "constant",
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        var resp = await sut.SendAsync(Req);

        Assert.Null(resp.Error);
        Assert.Equal("recovered", resp.Text);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesToCaller()
    {
        var inner = StubFrontierClient.DelaysThenSucceeds(TimeSpan.FromSeconds(30));
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 0,
            BreakAfterConsecutiveFailures = 10
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SendAsync(Req, cts.Token));
    }

    [Fact]
    public async Task NonTransientError_DoesNotOpenCircuit()
    {
        // auth failures must not count toward the circuit breaker;
        // circuit should remain closed after many consecutive auth errors
        const int breakAfter = 3;
        var inner = StubFrontierClient.AlwaysFails("auth", "unauthorized");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 0,
            BreakAfterConsecutiveFailures = breakAfter,
            BreakDurationSeconds = 300
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance);

        const int totalCalls = breakAfter + 2;
        for (var i = 0; i < totalCalls; i++)
            await sut.SendAsync(Req);

        // Every call must have reached the inner client - circuit never opened
        Assert.Equal(totalCalls, inner.CallCount);
    }

    [Fact]
    public async Task Fallback_ReturnsErrorWhenFallbackClientFails()
    {
        const int breakAfter = 2;
        var inner = StubFrontierClient.AlwaysFails("server_error", "primary failing");
        var fallback = StubFrontierClient.AlwaysFails("server_error", "fallback also failed");
        var opts = ResilienceOptions.LibraryDefault with
        {
            TimeoutSeconds = 60,
            MaxRetries = 0,
            BreakAfterConsecutiveFailures = breakAfter,
            BreakDurationSeconds = 300,
            FallbackStrategy = "fallback"
        };
        using var sut = new ResilientFrontierClient(
            inner, opts, NullLogger<ResilientFrontierClient>.Instance, fallback);

        for (var i = 0; i < breakAfter; i++)
            await sut.SendAsync(Req);

        var resp = await sut.SendAsync(Req);

        // Fallback was invoked, returned its own error - that error surfaces to caller
        Assert.NotNull(resp.Error);
        Assert.Equal("server_error", resp.Error!.ErrorCode);
        Assert.Equal(1, fallback.CallCount);
    }
}
