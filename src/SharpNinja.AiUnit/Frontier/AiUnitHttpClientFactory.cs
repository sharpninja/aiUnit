using System.Net.Http;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Public <see cref="IHttpClientFactory"/> implementation that hands out a
/// fresh <see cref="HttpClient"/> wrapping <see cref="HttpClientHandler"/> on
/// every <see cref="CreateClient"/> call. Default factory for aiUnit
/// consumers that do not register <see cref="IHttpClientFactory"/> via
/// <c>Microsoft.Extensions.Http</c>.
/// </summary>
public sealed class AiUnitHttpClientFactory : IHttpClientFactory
{
	/// <inheritdoc />
	public HttpClient CreateClient(string name)
	{
		var handler = new HttpClientHandler
		{
			AutomaticDecompression = System.Net.DecompressionMethods.All,
		};
		return new HttpClient(handler, disposeHandler: true);
	}
}
