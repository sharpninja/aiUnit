using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Tests.Resilience;

internal sealed class StubFrontierClient : IFrontierModelClient
{
    private readonly Func<FrontierRequest, CancellationToken, Task<FrontierResponse>> _handler;
    private int _callCount;

    public StubFrontierClient(
        Func<FrontierRequest, CancellationToken, Task<FrontierResponse>> handler,
        string provider = "stub",
        string modelVersion = "stub-model")
    {
        _handler = handler;
        Provider = provider;
        ModelVersion = modelVersion;
    }

    public string Provider { get; }
    public string ModelVersion { get; }
    public int CallCount => _callCount;

    public static StubFrontierClient AlwaysSucceeds(string text = "ok") =>
        new((_, _) => Task.FromResult(SuccessResponse(text)));

    public static StubFrontierClient AlwaysFails(string errorCode, string message = "stub error") =>
        new((_, _) => Task.FromResult(FailureResponse(errorCode, message)));

    public static StubFrontierClient ReturnsSequence(params FrontierResponse[] responses)
    {
        var queue = new Queue<FrontierResponse>(responses);
        return new StubFrontierClient((_, _) =>
            Task.FromResult(queue.Count > 0
                ? queue.Dequeue()
                : FailureResponse("server_error", "sequence exhausted")));
    }

    public static StubFrontierClient DelaysThenSucceeds(TimeSpan delay, string text = "ok") =>
        new(async (_, ct) =>
        {
            await Task.Delay(delay, ct);
            return SuccessResponse(text);
        });

    public async Task<FrontierResponse> SendAsync(
        FrontierRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        return await _handler(request, cancellationToken);
    }

    internal static FrontierResponse SuccessResponse(string text = "ok") =>
        new(text, FrontierTokenUsage.Zero, 0L, "stub", "stub-model", null, null);

    internal static FrontierResponse FailureResponse(string errorCode, string message = "stub error") =>
        new(null, FrontierTokenUsage.Zero, 0L, "stub", "stub-model", null,
            new FrontierError(errorCode, message, null));
}
