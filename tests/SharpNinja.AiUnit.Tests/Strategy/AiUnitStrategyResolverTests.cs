using System;
using System.Collections.Generic;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Strategy;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Strategy;

/// <summary>
/// Smoke tests for the strategy-based aiUnit config. Verifies that
/// <c>appsettings.aiunit.json</c> in the test output dir parses correctly,
/// that env vars override the active strategy, and that the resolver reports
/// the expected skip reason when an API key is absent.
///
/// Each test snapshots + clears every AIUNIT_* env var on entry and restores
/// them on exit so concurrent test classes that mutate the same env vars
/// (e.g. attribute-integration tests in another agent's scope) cannot leak
/// into the assertions here.
/// </summary>
[Collection("StrategyEnvironment")]
public sealed class AiUnitStrategyResolverTests
{
	private static readonly string[] AiUnitEnvVars =
	{
		"AIUNIT_STRATEGY",
		"AIUNIT_KIND",
		"AIUNIT_BASE_URL",
		"AIUNIT_MODEL",
		"AIUNIT_COMMAND",
		"AIUNIT_API_KEY",
		"AIUNIT_TIMEOUT_SECONDS",
		"AIUNIT_TEMPERATURE",
		"ANTHROPIC_API_KEY",
		"OPENAI_API_KEY",
		"GOOGLE_API_KEY",
		"XAI_API_KEY",
		"MAF_API_KEY",
		"COPILOT_API_KEY",
	};

	private static IDictionary<string, string?> SnapshotAndClear()
	{
		var snap = new Dictionary<string, string?>(AiUnitEnvVars.Length);
		foreach (var v in AiUnitEnvVars)
		{
			snap[v] = Environment.GetEnvironmentVariable(v);
			Environment.SetEnvironmentVariable(v, null);
		}
		return snap;
	}

	private static void Restore(IDictionary<string, string?> snap)
	{
		foreach (var kv in snap)
		{
			Environment.SetEnvironmentVariable(kv.Key, kv.Value);
		}
	}

	[Fact]
	public void TryLoad_FindsBundledAppsettings_WithAllStrategies()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.NotEmpty(config!.Strategies);
			Assert.Contains("maf", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("maf-grok", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("claude", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("claude-code-opus", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("claude-api", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("grok", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("codex", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("codex-subscription", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("codex-api", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("copilot-gemini", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("grok-build", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
			Assert.Contains("gemini", config.Strategies.Keys, StringComparer.OrdinalIgnoreCase);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_ClaudeCli_NoApiKeyNeeded_BuildsCliClient_WithSonnet46Model()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("claude", out var claudeCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("claude", claudeCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("cli", resolved.Kind);
			// claude strategy is pinned to Sonnet 4.6 by the bundled appsettings.
			Assert.Equal("claude-sonnet-4-6", resolved.Model);
			Assert.Equal("claude-sonnet-4-6", client!.ModelVersion);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_ClaudeCodeOpusCli_NoApiKeyNeeded_BuildsCliClient_WithOpusModel()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("claude-code-opus", out var claudeCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("claude-code-opus", claudeCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("cli", resolved.Kind);
			Assert.Equal("claude-opus-4-5", resolved.Model);
			Assert.Equal("claude-opus-4-5", client!.ModelVersion);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_CopilotGeminiCli_NoApiKeyNeeded_BuildsCliClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("copilot-gemini", out var copilotCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("copilot-gemini", copilotCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("cli", resolved.Kind);
			Assert.Equal("gemini-2.5-pro", resolved.Model);
			Assert.Equal("gemini-2.5-pro", client!.ModelVersion);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_CodexCli_NoApiKeyNeeded_BuildsCliClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("codex", out var codexCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("codex", codexCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("cli", resolved.Kind);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_GrokBuildCli_NoApiKeyNeeded_BuildsCliClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("grok-build", out var grokBuildCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("grok-build", grokBuildCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("cli", resolved.Kind);
			Assert.Equal("grok-4.3", resolved.Model);
			Assert.Equal("grok-4.3", client!.ModelVersion);
			Assert.Equal("grok-build:SharpNinja.AiUnit.GrokBridge.exe", client.Provider);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_CodexApiStrategy_WithApiKey_BuildsOpenAiCompatibleClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			Environment.SetEnvironmentVariable("AIUNIT_API_KEY", "test-key-codex");
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("codex-api", out var codexCfg));
			var (client, resolved, _) = AiUnitStrategyResolver.Build("codex-api", codexCfg);
			Assert.NotNull(client);
			Assert.Equal("openai-compatible", resolved.Kind);
			Assert.Equal("gpt-5-codex", resolved.Model);
			Assert.Equal("https://api.openai.com", resolved.BaseUrl);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void ResolveActive_DefaultsToConfiguredStrategy()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			var (name, cfg) = AiUnitStrategyLoader.ResolveActive(config);
			Assert.Equal(config!.ActiveStrategy, name);
			Assert.NotNull(cfg);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void ResolveActive_EnvVarOverridesJsonActive()
	{
		var snap = SnapshotAndClear();
		try
		{
			Environment.SetEnvironmentVariable("AIUNIT_STRATEGY", "grok");
			var config = AiUnitStrategyLoader.TryLoad();
			var (name, cfg) = AiUnitStrategyLoader.ResolveActive(config);
			Assert.Equal("grok", name);
			Assert.NotNull(cfg);
			Assert.Equal("openai-compatible", cfg!.Kind);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_ApiStrategy_WithoutApiKey_ReturnsSkipReason()
	{
		// claude-api Kind=anthropic requires a key; clear + verify skip path.
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("claude-api", out var claudeCfg));
			var (client, _, skipReason) = AiUnitStrategyResolver.Build("claude-api", claudeCfg);
			Assert.Null(client);
			Assert.Contains("API key", skipReason, StringComparison.OrdinalIgnoreCase);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_OpenAiCompatibleStrategy_WithApiKey_BuildsClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			Environment.SetEnvironmentVariable("AIUNIT_API_KEY", "test-key-1234");
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("grok", out var grokCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("grok", grokCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("openai-compatible", resolved.Kind);
			Assert.Equal("grok-4", resolved.Model);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_AnthropicApiStrategy_WithApiKey_BuildsClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			Environment.SetEnvironmentVariable("AIUNIT_API_KEY", "test-key-claude");
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("claude-api", out var claudeCfg));
			var (client, resolved, _) = AiUnitStrategyResolver.Build("claude-api", claudeCfg);
			Assert.NotNull(client);
			Assert.Equal("anthropic", resolved.Kind);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_GeminiStrategy_WithApiKey_BuildsGeminiClient()
	{
		var snap = SnapshotAndClear();
		try
		{
			Environment.SetEnvironmentVariable("AIUNIT_API_KEY", "test-key-gemini");
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("gemini", out var geminiCfg));
			var (client, resolved, skipReason) = AiUnitStrategyResolver.Build("gemini", geminiCfg);
			Assert.Equal(string.Empty, skipReason);
			Assert.NotNull(client);
			Assert.Equal("gemini", resolved.Kind);
			Assert.Equal("google", client!.Provider);
			Assert.Equal("gemini-2.5-flash", resolved.Model);
		}
		finally { Restore(snap); }
	}

	[Fact]
	public void Build_GeminiStrategy_WithoutApiKey_ReturnsSkipReason()
	{
		var snap = SnapshotAndClear();
		try
		{
			var config = AiUnitStrategyLoader.TryLoad();
			Assert.NotNull(config);
			Assert.True(config!.Strategies.TryGetValue("gemini", out var geminiCfg));
			var (client, _, skipReason) = AiUnitStrategyResolver.Build("gemini", geminiCfg);
			Assert.Null(client);
			Assert.Contains("API key", skipReason, StringComparison.OrdinalIgnoreCase);
		}
		finally { Restore(snap); }
	}
}
