using System;
using System.Collections.Generic;
using SharpNinja.AiUnit.Strategy;
using SharpNinja.AiUnit.Xunit;

namespace SharpNinja.AiUnit.Review;

internal sealed class DefaultAiReviewClientResolver : IAiReviewClientResolver
{
	public AiReviewResolvedClient ResolveDefault()
	{
		var fixture = AiStrategyFixture.Default;
		var name = TryGetResolvedName(fixture.Resolved) ?? "default";
		return new AiReviewResolvedClient(name, fixture.Client, fixture.SkipReason);
	}

	public AiReviewResolvedClient Resolve(AiReviewAgentSpec spec)
	{
		var name = string.IsNullOrWhiteSpace(spec.Name) ? "inline" : spec.Name!.Trim();
		var config = AiUnitStrategyLoader.TryLoad();
		AiUnitStrategySettings? configured = null;
		config?.Strategies.TryGetValue(name, out configured);
		var settings = MergeSettings(configured, spec);
		var (client, _, skipReason) = AiUnitStrategyResolver.Build(name, settings);
		return new AiReviewResolvedClient(name, client, skipReason);
	}

	private static AiUnitStrategySettings? MergeSettings(AiUnitStrategySettings? configured, AiReviewAgentSpec spec)
	{
		if (!spec.HasInlineDetails)
		{
			return configured;
		}

		var fallback = configured ?? new AiUnitStrategySettings(Kind: spec.Kind ?? string.Empty);
		return fallback with
		{
			Kind = spec.Kind ?? fallback.Kind,
			BaseUrl = spec.BaseUrl ?? fallback.BaseUrl,
			Model = spec.Model ?? fallback.Model,
			ApiKeyEnvVar = spec.ApiKeyEnvVar ?? fallback.ApiKeyEnvVar,
			Command = spec.Command ?? fallback.Command,
			TimeoutSeconds = spec.TimeoutSeconds is > 0 ? spec.TimeoutSeconds.Value : fallback.TimeoutSeconds,
			Temperature = spec.Temperature ?? fallback.Temperature,
		};
	}

	private static string? TryGetResolvedName(object? resolved)
	{
		if (resolved is ResolvedStrategy typed)
		{
			return typed.Name;
		}
		var prop = resolved?.GetType().GetProperty("Name");
		return prop?.GetValue(resolved) as string;
	}
}
