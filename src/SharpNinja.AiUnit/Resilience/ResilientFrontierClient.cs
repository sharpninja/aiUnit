using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using Polly.Retry;
using Polly.Timeout;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Resilience;

/// <summary>
/// Decorator that wraps any <see cref="IFrontierModelClient"/> with a Polly v8
/// resilience pipeline: per-attempt timeout, retry with backoff, circuit breaker,
/// and optional fallback to an alternate client. Pipeline disabled entirely when
/// <see cref="ResilienceOptions.ResilienceEnabled"/> is false.
/// </summary>
public sealed class ResilientFrontierClient : IFrontierModelClient, IDisposable
{
    private static readonly string[] NonTransientCodes =
        ["auth", "rate_limit", "AttachmentTooLarge", "spawn_failed"];

    private readonly IFrontierModelClient _inner;
    private readonly IFrontierModelClient? _fallback;
    private readonly ResilienceOptions _opts;
    private readonly ILogger<ResilientFrontierClient> _log;
    private readonly ResiliencePipeline<FrontierResponse>? _pipeline;

    // Passes the current request into Polly's FallbackAction closure.
    private readonly AsyncLocal<FrontierRequest?> _currentRequest = new();

    public ResilientFrontierClient(
        IFrontierModelClient inner,
        ResilienceOptions options,
        ILogger<ResilientFrontierClient> logger,
        IFrontierModelClient? fallback = null)
    {
        _inner = inner;
        _opts = options;
        _log = logger;
        _fallback = fallback;

        if (options.ResilienceEnabled)
            _pipeline = BuildPipeline(options);
    }

    public string Provider => _inner.Provider;
    public string ModelVersion => _inner.ModelVersion;

    public async Task<FrontierResponse> SendAsync(
        FrontierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_pipeline is null)
            return await _inner.SendAsync(request, cancellationToken);

        _currentRequest.Value = request;
        try
        {
            return await _pipeline.ExecuteAsync(
                static (state, ct) => new ValueTask<FrontierResponse>(
                    state.Inner.SendAsync(state.Request, ct)),
                new ExecuteState(_inner, request),
                cancellationToken);
        }
        catch (BrokenCircuitException bce) when (_fallback is null)
        {
            _log.LogWarning("Circuit open, no fallback configured: {Msg}", bce.Message);
            return CircuitOpenResponse(bce.Message);
        }
        catch (IsolatedCircuitException ice) when (_fallback is null)
        {
            _log.LogWarning("Circuit isolated, no fallback configured: {Msg}", ice.Message);
            return CircuitOpenResponse(ice.Message);
        }
        catch (TimeoutRejectedException tre)
        {
            _log.LogWarning("Per-attempt timeout exceeded: {Msg}", tre.Message);
            return TimeoutResponse(tre.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected exception in resilient client");
            return UnexpectedResponse(ex.Message);
        }
        finally
        {
            _currentRequest.Value = null;
        }
    }

    public void Dispose()
    {
        (_inner as IDisposable)?.Dispose();
        (_fallback as IDisposable)?.Dispose();
    }

    private ResiliencePipeline<FrontierResponse> BuildPipeline(ResilienceOptions opts)
    {
        var builder = new ResiliencePipelineBuilder<FrontierResponse>();

        // Outermost: fallback catches open-circuit exceptions and routes to alternate client.
        if (_fallback is not null && opts.FallbackStrategy is not null)
        {
            builder.AddFallback(new FallbackStrategyOptions<FrontierResponse>
            {
                ShouldHandle = new PredicateBuilder<FrontierResponse>()
                    .Handle<BrokenCircuitException>()
                    .Handle<IsolatedCircuitException>(),
                FallbackAction = async args =>
                {
                    var req = _currentRequest.Value;
                    if (req is null)
                        return Outcome.FromResult(UnexpectedResponse("fallback invoked with null request"));

                    _log.LogInformation("Fallback routing to alternate strategy");
                    var result = await _fallback.SendAsync(req, args.Context.CancellationToken);
                    return Outcome.FromResult(result);
                }
            });
        }

        // Second: circuit breaker surrounds the retry+timeout combo.
        // FailureRatio=1.0 + MinimumThroughput=N approximates "N consecutive failures".
        var samplingDuration = TimeSpan.FromSeconds(
            opts.BreakAfterConsecutiveFailures * Math.Max(opts.TimeoutSeconds, 30) + 60);

        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<FrontierResponse>
        {
            ShouldHandle = new PredicateBuilder<FrontierResponse>()
                .Handle<TimeoutRejectedException>()
                .HandleResult(r => r.Error is not null && IsTransient(r.Error.ErrorCode)),
            FailureRatio = 1.0,
            MinimumThroughput = opts.BreakAfterConsecutiveFailures,
            SamplingDuration = samplingDuration,
            BreakDuration = TimeSpan.FromSeconds(opts.BreakDurationSeconds),
            OnOpened = args =>
            {
                _log.LogWarning("Circuit breaker opened for {Duration}s", opts.BreakDurationSeconds);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                _log.LogInformation("Circuit breaker closed (recovered)");
                return ValueTask.CompletedTask;
            }
        });

        // Third: retry with configurable backoff. Skip when MaxRetries=0 (Polly requires >= 1).
        if (opts.MaxRetries >= 1)
        {
            builder.AddRetry(new RetryStrategyOptions<FrontierResponse>
            {
                ShouldHandle = new PredicateBuilder<FrontierResponse>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => r.Error is not null && IsTransient(r.Error.ErrorCode)),
                MaxRetryAttempts = opts.MaxRetries,
                BackoffType = ParseBackoff(opts.RetryBackoff),
                Delay = TimeSpan.FromMilliseconds(opts.RetryBaseDelayMs),
                UseJitter = opts.RetryBackoff.Equals("exponential", StringComparison.OrdinalIgnoreCase),
                OnRetry = args =>
                {
                    _log.LogInformation(
                        "Retry attempt {Attempt} after {Delay}ms",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            });
        }

        // Innermost: per-attempt timeout.
        builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds),
            OnTimeout = args =>
            {
                _log.LogWarning("Attempt timed out after {Timeout}s", opts.TimeoutSeconds);
                return ValueTask.CompletedTask;
            }
        });

        return builder.Build();
    }

    private static bool IsTransient(string errorCode)
    {
        foreach (var code in NonTransientCodes)
            if (string.Equals(errorCode, code, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static DelayBackoffType ParseBackoff(string backoff) =>
        backoff.ToLowerInvariant() switch
        {
            "linear" => DelayBackoffType.Linear,
            "constant" => DelayBackoffType.Constant,
            _ => DelayBackoffType.Exponential
        };

    private FrontierResponse CircuitOpenResponse(string message) =>
        new(null, FrontierTokenUsage.Zero, 0L, _inner.Provider, _inner.ModelVersion,
            null, new FrontierError("circuit_open", message, null));

    private FrontierResponse TimeoutResponse(string message) =>
        new(null, FrontierTokenUsage.Zero, 0L, _inner.Provider, _inner.ModelVersion,
            null, new FrontierError("timeout", message, null));

    private FrontierResponse UnexpectedResponse(string message) =>
        new(null, FrontierTokenUsage.Zero, 0L, _inner.Provider, _inner.ModelVersion,
            null, new FrontierError("unexpected", message, null));

    private readonly record struct ExecuteState(IFrontierModelClient Inner, FrontierRequest Request);
}
