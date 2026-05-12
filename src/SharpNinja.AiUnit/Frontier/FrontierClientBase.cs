using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Shared transport pipeline for the three HTTP frontier adapters. Concrete
/// clients inherit and supply: per-provider request URL, header builder, body
/// serializer, response parser, cost rate table, and provider name.
/// </summary>
public abstract class FrontierClientBase
{
	private const int RetryableStatus503 = (int)HttpStatusCode.ServiceUnavailable;
	private const int RetryableStatus502 = (int)HttpStatusCode.BadGateway;
	private const int RetryableStatus504 = (int)HttpStatusCode.GatewayTimeout;
	private const string HttpClientName = "frontier";

	/// <summary>HTTP factory injected at construction. Per-call clients dispose normally.</summary>
	protected readonly IHttpClientFactory HttpClientFactory;

	/// <summary>Per-provider configuration (api-key, base url, model, timeout).</summary>
	protected readonly IFrontierProviderConfig Config;

	private readonly ILogger _logger;

	/// <summary>Common constructor; subclasses forward their typed logger.</summary>
	protected FrontierClientBase(
		IHttpClientFactory httpClientFactory,
		IFrontierProviderConfig config,
		ILogger logger)
	{
		HttpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
		Config = config ?? throw new ArgumentNullException(nameof(config));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <summary>Lower-case short provider name (e.g. "anthropic", "openai", "xai", "google").</summary>
	public abstract string Provider { get; }

	/// <summary>Configured model id, surfaced to callers for telemetry.</summary>
	public string ModelVersion => Config.ModelVersion;

	/// <summary>
	/// Implementations build the per-provider HTTP request: URL, headers, JSON
	/// body. The pipeline owns sending, retry, and response parsing.
	/// </summary>
	protected abstract HttpRequestMessage BuildRequest(FrontierRequest request);

	/// <summary>
	/// Provider-specific success-body parser. Return (text, usage) on success.
	/// Throw <see cref="System.Text.Json.JsonException"/> on malformed shape.
	/// </summary>
	protected abstract (string Text, FrontierTokenUsage Usage) ParseSuccessBody(string body);

	/// <summary>
	/// Provider-specific cost estimator. Static rate table embedded per adapter.
	/// </summary>
	protected abstract decimal? EstimateCostUsd(FrontierTokenUsage usage);

	/// <summary>
	/// Optional: providers that authenticate via query-string (Gemini) override
	/// this to redact the api-key parameter for logging. Default returns the
	/// request URI's path only, safe for header-auth providers.
	/// </summary>
	protected virtual string SafeRequestUri(HttpRequestMessage request)
		=> request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "<no-uri>";

	/// <summary>
	/// Submit a request to the provider with built-in retry on 502/503/504 and
	/// error normalization. Never throws except for caller-cancelled
	/// <see cref="OperationCanceledException"/>.
	/// </summary>
	public async Task<FrontierResponse> SendAsync(
		FrontierRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		var sw = System.Diagnostics.Stopwatch.StartNew();

		// Reject oversize attachments before any network call.
		if (request.Attachments is { Count: > 0 } attachments)
		{
			foreach (var att in attachments)
			{
				if (att is null) continue;
				if ((att.Data?.Length ?? 0) > FrontierAttachment.MaxSizeBytes)
				{
					return new FrontierResponse(
						Text: null,
						TokenUsage: FrontierTokenUsage.Zero,
						LatencyMs: sw.ElapsedMilliseconds,
						Provider: Provider,
						ModelVersion: ModelVersion,
						EstimatedCostUsd: null,
						Error: new FrontierError(
							ErrorCode: "AttachmentTooLarge",
							Message: $"Attachment '{att.Name}' is {att.Data?.Length ?? 0} bytes; max is {FrontierAttachment.MaxSizeBytes}.",
							HttpStatus: null));
				}
			}
		}

		HttpResponseMessage? response = null;
		try
		{
			response = await SendWithRetryAsync(request, cancellationToken).ConfigureAwait(false);
			var status = (int)response.StatusCode;
			var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				var errCode = MapStatusToErrorCode(status);
				var safeUri = response.RequestMessage is { } rm ? SafeRequestUri(rm) : "<no-request>";
				_logger.LogWarning(
					"Frontier {Provider} call failed: status={Status} latencyMs={Latency} uri={Uri}",
					Provider, status, sw.ElapsedMilliseconds, safeUri);
				var bodyExcerpt = string.IsNullOrEmpty(body)
					? string.Empty
					: ": " + (body.Length > 500 ? body.Substring(0, 500) + "..." : body);
				return new FrontierResponse(
					Text: null,
					TokenUsage: FrontierTokenUsage.Zero,
					LatencyMs: sw.ElapsedMilliseconds,
					Provider: Provider,
					ModelVersion: ModelVersion,
					EstimatedCostUsd: null,
					Error: new FrontierError(errCode, $"HTTP {status}{bodyExcerpt}", status));
			}

			var (text, usage) = ParseSuccessBody(body);
			var cost = EstimateCostUsd(usage);
			_logger.LogInformation(
				"Frontier {Provider} ok: status=200 latencyMs={Latency} inTokens={In} outTokens={Out}",
				Provider, sw.ElapsedMilliseconds, usage.InputTokens, usage.OutputTokens);
			return new FrontierResponse(
				Text: text,
				TokenUsage: usage,
				LatencyMs: sw.ElapsedMilliseconds,
				Provider: Provider,
				ModelVersion: ModelVersion,
				EstimatedCostUsd: cost,
				Error: null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (TaskCanceledException ex)
		{
			_logger.LogWarning("Frontier {Provider} timed out after {Latency}ms", Provider, sw.ElapsedMilliseconds);
			return Failure(sw, "timeout", ex.Message, null);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogWarning(ex, "Frontier {Provider} network error after {Latency}ms", Provider, sw.ElapsedMilliseconds);
			return Failure(sw, "network", ex.Message, (int?)ex.StatusCode);
		}
		catch (System.Text.Json.JsonException ex)
		{
			_logger.LogWarning(ex, "Frontier {Provider} returned malformed JSON after {Latency}ms", Provider, sw.ElapsedMilliseconds);
			return Failure(sw, "malformed_response", ex.Message, null);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Frontier {Provider} unexpected exception after {Latency}ms", Provider, sw.ElapsedMilliseconds);
			return Failure(sw, "unexpected", ex.GetType().Name, null);
		}
		finally
		{
			response?.Dispose();
		}
	}

	private async Task<HttpResponseMessage> SendWithRetryAsync(FrontierRequest request, CancellationToken ct)
	{
		var http = HttpClientFactory.CreateClient(HttpClientName);
		http.Timeout = Config.Timeout > TimeSpan.Zero ? Config.Timeout : TimeSpan.FromSeconds(15);

		var attempt = BuildRequest(request);
		HttpResponseMessage response;
		try
		{
			response = await http.SendAsync(attempt, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
		}
		catch (HttpRequestException) when (!ct.IsCancellationRequested)
		{
			// Single retry on network failure.
			var retry = BuildRequest(request);
			response = await http.SendAsync(retry, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
			return response;
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			// Single retry on timeout (HttpClient surfaces timeout as TaskCanceled when
			// the caller-supplied token did not cancel).
			var retry = BuildRequest(request);
			response = await http.SendAsync(retry, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
			return response;
		}

		var status = (int)response.StatusCode;
		if (status == RetryableStatus503 || status == RetryableStatus502 || status == RetryableStatus504)
		{
			response.Dispose();
			var retry = BuildRequest(request);
			response = await http.SendAsync(retry, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
		}
		return response;
	}

	private FrontierResponse Failure(System.Diagnostics.Stopwatch sw, string code, string message, int? status)
		=> new(
			Text: null,
			TokenUsage: FrontierTokenUsage.Zero,
			LatencyMs: sw.ElapsedMilliseconds,
			Provider: Provider,
			ModelVersion: ModelVersion,
			EstimatedCostUsd: null,
			Error: new FrontierError(code, message, status));

	private static string MapStatusToErrorCode(int status) => status switch
	{
		401 or 403 => "auth",
		429 => "rate_limit",
		>= 500 => "server_error",
		_ => "server_error",
	};
}
