using System;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Per-provider configuration consumed by adapters. The active
/// <see cref="ApiKey"/> is provided by the strategy layer (resolved from an
/// env var named in <c>ApiKeyEnvVar</c>) - this interface does not own
/// credential storage.
/// </summary>
public interface IFrontierProviderConfig
{
	/// <summary>Raw bearer / x-api-key / query-param credential. NEVER logged.</summary>
	string ApiKey { get; }

	/// <summary>Fully-qualified model identifier (provider-specific).</summary>
	string ModelVersion { get; }

	/// <summary>Provider base URL. Adapters append their canonical path.</summary>
	Uri BaseUrl { get; }

	/// <summary>Per-request HTTP timeout. Default in adapters is 15s.</summary>
	TimeSpan Timeout { get; }
}

/// <summary>
/// Plain record implementation of <see cref="IFrontierProviderConfig"/>.
/// Strategy resolver materialises this from JSON + env-var merge.
/// </summary>
public sealed record FrontierProviderConfig(
	string ApiKey,
	string ModelVersion,
	Uri BaseUrl,
	TimeSpan Timeout) : IFrontierProviderConfig;
