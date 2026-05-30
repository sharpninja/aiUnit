using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Review;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// Shared test doubles for the review run-log suites. Kept separate from
/// <c>AiReviewAttributeTests</c> private fakes so the run-log tests can be
/// authored independently.
/// </summary>
internal static class ReviewJsonSamples
{
	public static string Pass(string agent, string summary = "ok") =>
		$$"""
		{
		  "schemaVersion": "{{AiReviewFindingsSchema.SchemaVersion}}",
		  "reviewType": "code",
		  "status": "pass",
		  "summary": "{{summary}}",
		  "agent": { "name": "{{agent}}" },
		  "findings": []
		}
		""";
}

internal sealed class RecordingRunLogSink : IAiReviewRunLogSink
{
	private readonly string _path;
	private readonly string? _url;

	public RecordingRunLogSink(string path, string? url)
	{
		_path = path;
		_url = url;
	}

	public List<AiReviewRunLogEntry> Entries { get; } = [];

	public AiReviewRunLogRef Write(AiReviewRunLogEntry entry)
	{
		Entries.Add(entry);
		return new AiReviewRunLogRef(_path, _url, entry.StartedUtc);
	}
}

internal sealed class StubReviewResolver : IAiReviewClientResolver
{
	public StubReviewClient DefaultClient { get; } = new("default", _ => ReviewJsonSamples.Pass("default"));

	public Dictionary<string, StubReviewClient> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);

	public AiReviewResolvedClient ResolveDefault() => new("default", DefaultClient, null);

	public AiReviewResolvedClient Resolve(AiReviewAgentSpec spec)
	{
		var name = string.IsNullOrWhiteSpace(spec.Name) ? "inline" : spec.Name!;
		return Clients.TryGetValue(name, out var client)
			? new AiReviewResolvedClient(name, client, null)
			: new AiReviewResolvedClient(name, null, $"Missing {name}");
	}
}

internal sealed class StubReviewClient : IFrontierModelClient
{
	public StubReviewClient(string provider, Func<FrontierRequest, string> responseFactory)
	{
		Provider = provider;
		ResponseFactory = responseFactory;
	}

	public string Provider { get; }

	public string ModelVersion => "fake-model";

	public Func<FrontierRequest, string> ResponseFactory { get; set; }

	public List<FrontierRequest> Requests { get; } = [];

	public Task<FrontierResponse> SendAsync(FrontierRequest request, CancellationToken cancellationToken = default)
	{
		_ = cancellationToken;
		Requests.Add(request);
		return Task.FromResult(new FrontierResponse(
			ResponseFactory(request),
			FrontierTokenUsage.Zero,
			LatencyMs: 1,
			Provider,
			ModelVersion,
			EstimatedCostUsd: null,
			Error: null));
	}
}
