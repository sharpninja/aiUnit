using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Review;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// WS-B: the three review attributes must never run in parallel in a single
/// process. Verifies the process-wide gate in <see cref="AiReviewExecutor"/>
/// serializes concurrent executions, and that the shipped
/// <see cref="AiReviewCollection"/> declares a serial, named xUnit collection.
/// </summary>
public sealed class AiReviewSerializationTests
{
	[Fact]
	public async Task ConcurrentReviews_AreSerialized_AtMostOneAgentCallAtATime()
	{
		var probe = new ConcurrencyProbeClient(delayMs: 25);
		var resolver = new ProbeResolver(probe);
		var request = new AiReviewExecutionRequest(
			AiReviewKind.Code, "Review the diff.", Array.Empty<AiReviewAgentSpec>());

		// Each task gets its own sink so the assertion is purely about agent-call concurrency.
		var tasks = Enumerable.Range(0, 8)
			.Select(_ => AiReviewExecutor.ExecuteAsync(request, resolver, new RecordingRunLogSink("ignored", null)))
			.ToArray();
		await Task.WhenAll(tasks);

		Assert.Equal(8, probe.Calls);
		Assert.Equal(1, probe.MaxObservedConcurrency);
	}

	[Fact]
	public void AiReviewCollection_DeclaresSerialNamedCollection()
	{
		var data = CustomAttributeData.GetCustomAttributes(typeof(AiReviewCollection))
			.Single(d => d.AttributeType == typeof(global::Xunit.CollectionDefinitionAttribute));

		Assert.Equal("aiUnit AI Reviews", AiReviewCollection.Name);
		Assert.Equal(AiReviewCollection.Name, (string?)data.ConstructorArguments[0].Value);
		var disable = data.NamedArguments.Single(a => a.MemberName == "DisableParallelization").TypedValue.Value;
		Assert.True((bool)disable!);
	}

	private sealed class ConcurrencyProbeClient : IFrontierModelClient
	{
		private readonly int _delayMs;
		private int _inFlight;
		private int _maxObserved;
		private int _calls;

		public ConcurrencyProbeClient(int delayMs) => _delayMs = delayMs;

		public int MaxObservedConcurrency => Volatile.Read(ref _maxObserved);

		public int Calls => Volatile.Read(ref _calls);

		public string Provider => "probe";

		public string ModelVersion => "probe-model";

		public async Task<FrontierResponse> SendAsync(FrontierRequest request, CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _calls);
			var now = Interlocked.Increment(ref _inFlight);
			UpdateMax(now);
			try
			{
				await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				Interlocked.Decrement(ref _inFlight);
			}

			return new FrontierResponse(
				ReviewJsonSamples.Pass("probe"),
				FrontierTokenUsage.Zero,
				LatencyMs: 1,
				Provider,
				ModelVersion,
				EstimatedCostUsd: null,
				Error: null);
		}

		private void UpdateMax(int candidate)
		{
			int observed;
			do
			{
				observed = Volatile.Read(ref _maxObserved);
				if (candidate <= observed)
				{
					return;
				}
			}
			while (Interlocked.CompareExchange(ref _maxObserved, candidate, observed) != observed);
		}
	}

	private sealed class ProbeResolver : IAiReviewClientResolver
	{
		private readonly IFrontierModelClient _client;

		public ProbeResolver(IFrontierModelClient client) => _client = client;

		public AiReviewResolvedClient ResolveDefault() => new("probe", _client, null);

		public AiReviewResolvedClient Resolve(AiReviewAgentSpec spec) =>
			new(string.IsNullOrWhiteSpace(spec.Name) ? "probe" : spec.Name!, _client, null);
	}
}
