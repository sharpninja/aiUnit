using System.Net.Http;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Test-only IHttpClientFactory that returns a fresh HttpClient wrapping the
/// provided FakeHttpMessageHandler on every CreateClient call.
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
	private readonly FakeHttpMessageHandler _handler;
	public FakeHttpClientFactory(FakeHttpMessageHandler handler) => _handler = handler;

	public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
