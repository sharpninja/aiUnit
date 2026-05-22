using System;
using System.IO;
using System.Text.Json.Nodes;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitStrategyConfigEditorTests
{
	[Fact]
	public void SetActiveStrategy_UpdatesValueAndPreservesUnrelatedPropertiesAndOrder()
	{
		using var workspace = TempConfig.Create(SeedConfig());

		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);
		var changed = editor.TrySetActiveStrategy("codex", out var error);
		editor.Save();

		Assert.True(changed, error);
		var json = File.ReadAllText(workspace.Path);
		var root = JsonNode.Parse(json)!.AsObject();
		Assert.Equal("https://json-schema.org/draft/2020-12/schema", root["$schema"]!.GetValue<string>());
		Assert.Equal("keep-before", root["Before"]!.GetValue<string>());
		Assert.True(root["After"]!.GetValue<bool>());
		Assert.Equal("codex", root["AiUnit"]!["ActiveStrategy"]!.GetValue<string>());
		Assert.Equal("keep-aiunit", root["AiUnit"]!["OtherSetting"]!.GetValue<string>());
		Assert.True(json.IndexOf("\"$schema\"", StringComparison.Ordinal) < json.IndexOf("\"Before\"", StringComparison.Ordinal));
		Assert.True(json.IndexOf("\"Before\"", StringComparison.Ordinal) < json.IndexOf("\"AiUnit\"", StringComparison.Ordinal));
		Assert.True(json.IndexOf("\"AiUnit\"", StringComparison.Ordinal) < json.IndexOf("\"After\"", StringComparison.Ordinal));
		var strategiesJson = root["AiUnit"]!["Strategies"]!.ToJsonString();
		Assert.True(strategiesJson.IndexOf("\"claude\"", StringComparison.Ordinal) < strategiesJson.IndexOf("\"codex\"", StringComparison.Ordinal));
	}

	[Fact]
	public void AddOrUpdateStrategy_AddsNewStrategyAfterExistingEntries()
	{
		using var workspace = TempConfig.Create(SeedConfig());

		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);
		var changed = editor.TryAddOrUpdateStrategy(
			"gemini",
			new AiUnitStrategyDefinition(
				Kind: "gemini",
				BaseUrl: "https://generativelanguage.googleapis.com",
				Model: "gemini-2.5-flash",
				ApiKeyEnvVar: "GOOGLE_API_KEY",
				TimeoutSeconds: 600,
				Temperature: 0.0),
			out var error);
		editor.Save();

		Assert.True(changed, error);
		var json = File.ReadAllText(workspace.Path);
		Assert.True(json.IndexOf("\"codex\"", StringComparison.Ordinal) < json.IndexOf("\"gemini\"", StringComparison.Ordinal));
		var strategy = JsonNode.Parse(json)!["AiUnit"]!["Strategies"]!["gemini"]!;
		Assert.Equal("gemini", strategy["Kind"]!.GetValue<string>());
		Assert.Equal("https://generativelanguage.googleapis.com", strategy["BaseUrl"]!.GetValue<string>());
		Assert.Equal("GOOGLE_API_KEY", strategy["ApiKeyEnvVar"]!.GetValue<string>());
	}

	[Fact]
	public void AddOrUpdateStrategy_UpdatesExistingStrategyWithoutRemovingOthers()
	{
		using var workspace = TempConfig.Create(SeedConfig());

		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);
		var changed = editor.TryAddOrUpdateStrategy(
			"codex",
			new AiUnitStrategyDefinition(
				Kind: "cli",
				Command: "codex",
				Model: "gpt-5-codex"),
			out var error);
		editor.Save();

		Assert.True(changed, error);
		var strategies = JsonNode.Parse(File.ReadAllText(workspace.Path))!["AiUnit"]!["Strategies"]!;
		Assert.NotNull(strategies["claude"]);
		Assert.Equal("gpt-5-codex", strategies["codex"]!["Model"]!.GetValue<string>());
	}

	[Fact]
	public void RemoveStrategy_RemovesNonActiveAndRejectsActiveStrategy()
	{
		using var workspace = TempConfig.Create(SeedConfig());
		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);

		var removed = editor.TryRemoveStrategy("codex", out var removeError);
		var activeRemoved = editor.TryRemoveStrategy("claude", out var activeError);
		editor.Save();

		Assert.True(removed, removeError);
		Assert.False(activeRemoved);
		Assert.Contains("Cannot remove active strategy", activeError);
		var strategies = JsonNode.Parse(File.ReadAllText(workspace.Path))!["AiUnit"]!["Strategies"]!;
		Assert.Null(strategies["codex"]);
		Assert.NotNull(strategies["claude"]);
	}

	[Fact]
	public void Validate_ReportsUnknownActiveStrategyAndMissingKind()
	{
		using var workspace = TempConfig.Create(
			"""
			{
			  "AiUnit": {
			    "ActiveStrategy": "missing",
			    "Strategies": {
			      "broken": {
			        "Command": "codex"
			      }
			    }
			  }
			}
			""");

		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);

		var messages = editor.Validate();

		Assert.Contains(messages, message => message.Contains("Active strategy 'missing'", StringComparison.Ordinal));
		Assert.Contains(messages, message => message.Contains("Strategy 'broken' is missing required Kind", StringComparison.Ordinal));
	}

	[Fact]
	public void SetActiveStrategy_ReturnsErrorWhenStrategyIsMissing()
	{
		using var workspace = TempConfig.Create(SeedConfig());
		var editor = AiUnitStrategyConfigEditor.Load(workspace.Path);

		var changed = editor.TrySetActiveStrategy("gemini", out var error);

		Assert.False(changed);
		Assert.Contains("is not configured", error);
	}

	private static string SeedConfig() =>
		"""
		{
		  "$schema": "https://json-schema.org/draft/2020-12/schema",
		  "Before": "keep-before",
		  "AiUnit": {
		    "ActiveStrategy": "claude",
		    "OtherSetting": "keep-aiunit",
		    "Strategies": {
		      "claude": {
		        "Kind": "cli",
		        "Command": "claude"
		      },
		      "codex": {
		        "Kind": "cli",
		        "Command": "codex"
		      }
		    }
		  },
		  "After": true
		}
		""";

	private sealed class TempConfig : IDisposable
	{
		private TempConfig(string root, string path)
		{
			Root = root;
			Path = path;
		}

		public string Root { get; }

		public string Path { get; }

		public static TempConfig Create(string content)
		{
			var root = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"aiunit-config-editor-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			var path = System.IO.Path.Combine(root, "appsettings.aiunit.json");
			File.WriteAllText(path, content);
			return new TempConfig(root, path);
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
