using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpNinja.AiUnit.Tests.Frontier;

/// <summary>
/// Test-only HttpMessageHandler that records the inbound request and returns a
/// canned response (or throws a canned exception). Used by every frontier
/// adapter test to assert request shape and feed response shape without
/// touching the network.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

	public List<HttpRequestMessage> Requests { get; } = new();
	public List<string> CapturedBodies { get; } = new();
	public int CallCount { get; private set; }

	public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
	{
		_send = send;
	}

	public static FakeHttpMessageHandler ReturnsJson(HttpStatusCode status, string body)
		=> new((_, _) => Task.FromResult(new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		}));

	public static FakeHttpMessageHandler ReturnsSequence(params HttpResponseMessage[] responses)
	{
		var queue = new Queue<HttpResponseMessage>(responses);
		return new FakeHttpMessageHandler((_, _) =>
			Task.FromResult(queue.Count > 0 ? queue.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError)));
	}

	public static FakeHttpMessageHandler Throws(Exception ex)
		=> new((_, _) => throw ex);

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		CallCount++;
		Requests.Add(request);
		string body = string.Empty;
		if (request.Content is not null)
		{
			body = await request.Content.ReadAsStringAsync(cancellationToken);
		}
		CapturedBodies.Add(body);
		return await _send(request, cancellationToken);
	}
}
