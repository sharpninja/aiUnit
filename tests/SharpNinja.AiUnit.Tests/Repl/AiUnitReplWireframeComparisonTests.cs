using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Xunit;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitReplWireframeComparisonTests
{
	[Fact]
	public void WireframeComparisonYamlScenarios_ContainTraceabilityImagesSchemaAndSanitizedAgentsContent()
	{
		var scenarios = AiUnitReplWireframeComparisonScenarioCatalog.LoadAll();

		Assert.Equal(4, scenarios.Count);
		Assert.Contains(scenarios, scenario => scenario.ScreenId == "workspace-overview");
		Assert.Contains(scenarios, scenario => scenario.ScreenId == "project-strategy-editor");
		Assert.Contains(scenarios, scenario => scenario.ScreenId == "strategy-catalog");
		Assert.Contains(scenarios, scenario => scenario.ScreenId == "validation-deploy");

		foreach (var scenario in scenarios)
		{
			Assert.NotEmpty(scenario.FunctionalRequirements);
			Assert.NotEmpty(scenario.TechnicalRequirements);
			Assert.All(scenario.FunctionalRequirements, requirement => Assert.StartsWith("FR-", requirement.Id, StringComparison.Ordinal));
			Assert.All(scenario.TechnicalRequirements, requirement => Assert.StartsWith("TR-", requirement.Id, StringComparison.Ordinal));
			Assert.Contains("\"$schema\"", scenario.ResultSchema, StringComparison.Ordinal);
			Assert.Contains("colorAccessibilityValidation", scenario.ResultSchema, StringComparison.Ordinal);
			Assert.Contains("wireframeSuitability", scenario.ResultSchema, StringComparison.Ordinal);
			using JsonDocument _ = JsonDocument.Parse(scenario.ResultSchema);

			Assert.Equal("AGENTS-README-FIRST.yaml", scenario.AgentsReadmeFirstPath);
			Assert.False(string.IsNullOrWhiteSpace(scenario.AgentsReadmeFirstContent));
			Assert.DoesNotContain("apiKey:", scenario.AgentsReadmeFirstContent, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("X-Api-Key", scenario.AgentsReadmeFirstContent, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("api_key=", scenario.AgentsReadmeFirstContent, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("agentsReadmeFirstContent: |", scenario.ModelPayloadYaml, StringComparison.Ordinal);

			Assert.True(File.Exists(scenario.ActualScreenshotFullPath), scenario.ActualScreenshotPath);
			Assert.True(File.Exists(scenario.WireframeScreenshotFullPath), scenario.WireframeScreenshotPath);
			Assert.True(File.Exists(scenario.WireframeSvgFullPath), scenario.WireframeSvgPath);
			Assert.Equal((1200, 760), PngProbe.ReadDimensions(scenario.ActualScreenshotFullPath));
			Assert.Equal((1200, 760), PngProbe.ReadDimensions(scenario.WireframeScreenshotFullPath));

			var userPrompt = AiUnitReplWireframeComparisonPrompt.BuildUserPrompt(scenario);
			Assert.Contains("YAML screen comparison scenario file content", userPrompt, StringComparison.Ordinal);
			Assert.Contains(scenario.ModelPayloadYaml, userPrompt, StringComparison.Ordinal);
			Assert.Contains(scenario.ActualScreenshotPath, userPrompt, StringComparison.Ordinal);
			Assert.Contains(scenario.WireframeScreenshotPath, userPrompt, StringComparison.Ordinal);
		}

		var instructions = AiUnitReplWireframeComparisonPrompt.BuildInstructions();
		Assert.Contains("first image is the wireframe baseline", instructions, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("second image is the actual finished TUI screenshot", instructions, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Return only valid JSON", instructions, StringComparison.Ordinal);
		Assert.Contains("colorAccessibilityValidation", instructions, StringComparison.Ordinal);
		Assert.Contains("wireframeSuitability", instructions, StringComparison.Ordinal);
	}

	[Fact]
	public void WireframeComparisonResultValidator_AcceptsCompleteStrictJson()
	{
		var json = """
		{
		  "screenId": "workspace-overview",
		  "summary": "The overview screen shows recursive discovery state and global commands.",
		  "correctElements": [
		    {
		      "element": "project table",
		      "requirement": "FR-AIUNITREPL-002",
		      "evidence": "The actual screenshot lists project names, active strategies, counts, and states.",
		      "confidence": 0.94
		    }
		  ],
		  "incorrectElements": [],
		  "colorAccessibilityValidation": {
		    "colorsMatchWireframe": true,
		    "adaCompliant": true,
		    "deviations": [],
		    "why": "The terminal screenshot uses high-contrast light text on a dark background with text labels for states."
		  },
		  "wireframeSuitability": {
		    "rating": "suitable",
		    "why": "The wireframe includes the required discovery table and global command areas.",
		    "missingRequirements": [],
		    "recommendedWireframeChanges": []
		  }
		}
		""";

		var result = AiUnitReplWireframeComparisonValidator.ParseAndValidate(json, "workspace-overview");

		Assert.Equal("workspace-overview", result.ScreenId);
		Assert.Single(result.CorrectElements);
		Assert.Empty(result.IncorrectElements);
		Assert.True(result.ColorAccessibilityValidation!.ColorsMatchWireframe);
		Assert.Equal("suitable", result.WireframeSuitability!.Rating);
	}

	[Fact]
	public void WireframeComparisonResultValidator_RejectsMissingRequiredSections()
	{
		var json = """
		{
		  "screenId": "workspace-overview",
		  "summary": "Incomplete.",
		  "correctElements": [],
		  "incorrectElements": []
		}
		""";

		var failure = Assert.Throws<InvalidDataException>(() =>
			AiUnitReplWireframeComparisonValidator.ParseAndValidate(json, "workspace-overview"));

		Assert.Contains("colorAccessibilityValidation", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void WireframeComparisonResultValidator_RejectsAdditionalRootProperties()
	{
		var json = """
		{
		  "screenId": "workspace-overview",
		  "summary": "Extra property should fail strict JSON.",
		  "correctElements": [
		    {
		      "element": "project table",
		      "requirement": "FR-AIUNITREPL-002",
		      "evidence": "Visible.",
		      "confidence": 0.9
		    }
		  ],
		  "incorrectElements": [],
		  "colorAccessibilityValidation": {
		    "colorsMatchWireframe": true,
		    "adaCompliant": true,
		    "deviations": [],
		    "why": "Readable."
		  },
		  "wireframeSuitability": {
		    "rating": "suitable",
		    "why": "Suitable.",
		    "missingRequirements": [],
		    "recommendedWireframeChanges": []
		  },
		  "markdown": "not allowed"
		}
		""";

		var failure = Assert.Throws<InvalidDataException>(() =>
			AiUnitReplWireframeComparisonValidator.ParseAndValidate(json, "workspace-overview"));

		Assert.Contains("Unexpected root property 'markdown'", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void WireframeComparisonResultValidator_RejectsFailedColorAccessibilityWithoutDeviation()
	{
		var json = """
		{
		  "screenId": "workspace-overview",
		  "summary": "Color failure lacks evidence.",
		  "correctElements": [
		    {
		      "element": "project table",
		      "requirement": "FR-AIUNITREPL-002",
		      "evidence": "Visible.",
		      "confidence": 0.9
		    }
		  ],
		  "incorrectElements": [],
		  "colorAccessibilityValidation": {
		    "colorsMatchWireframe": false,
		    "adaCompliant": false,
		    "deviations": [],
		    "why": "Problematic."
		  },
		  "wireframeSuitability": {
		    "rating": "suitable",
		    "why": "Suitable.",
		    "missingRequirements": [],
		    "recommendedWireframeChanges": []
		  }
		}
		""";

		var failure = Assert.Throws<InvalidDataException>(() =>
			AiUnitReplWireframeComparisonValidator.ParseAndValidate(json, "workspace-overview"));

		Assert.Contains("colorAccessibilityValidation.deviations", failure.Message, StringComparison.Ordinal);
	}

	[AiTheory]
	[MemberData(nameof(WireframeComparisonScenarioPaths))]
	public async Task AiUnitWireframeComparison_RunsYamlScenarioWhenEnabled(string scenarioPath)
	{
		if (!AiUnitReplWireframeComparisonOptions.Enabled)
		{
			Skip.If(true, "Set AIUNIT_REPL_VISUAL_COMPARISON_ENABLED=true to run live aiUnit TUI wireframe comparisons.");
		}

		AiSkip.IfNoStrategy();
		var scenario = AiUnitReplWireframeComparisonScenarioCatalog.Load(scenarioPath);
		var fixture = AiStrategyFixture.Default;
		Assert.NotNull(fixture.Client);

		var request = new FrontierRequest(
			SystemPrompt: AiUnitReplWireframeComparisonPrompt.BuildInstructions(),
			UserMessage: AiUnitReplWireframeComparisonPrompt.BuildUserPrompt(scenario),
			Attachments: AiUnitReplWireframeComparisonPrompt.BuildAttachments(scenario),
			Temperature: 0.0,
			RequireJsonOutput: true);

		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
		var response = await fixture.Client!.SendAsync(request, timeout.Token);

		if (response.Error is { } error)
		{
			Skip.If(
				string.Equals(error.ErrorCode, "auth", StringComparison.OrdinalIgnoreCase)
					|| error.HttpStatus is 401 or 403,
				error.Message);
			Assert.Fail(error.Message);
		}

		var result = AiUnitReplWireframeComparisonValidator.ParseAndValidate(
			response.Text ?? string.Empty,
			scenario.ScreenId);

		var outputDirectory = Path.Combine(AiUnitReplWireframeComparisonScenarioCatalog.RepositoryRoot, "artifacts", "aiunit-repl-wireframe-comparisons");
		Directory.CreateDirectory(outputDirectory);
		await File.WriteAllTextAsync(
			Path.Combine(outputDirectory, scenario.ScreenId + ".json"),
			JsonSerializer.Serialize(result, AiUnitReplWireframeComparisonJson.IndentedOptions),
			timeout.Token);

		Assert.NotEmpty(result.CorrectElements);
		Assert.Empty(result.IncorrectElements);
		Assert.True(result.ColorAccessibilityValidation!.ColorsMatchWireframe);
		Assert.True(result.ColorAccessibilityValidation.AdaCompliant);
		Assert.Empty(result.ColorAccessibilityValidation.Deviations);
		Assert.Equal("suitable", result.WireframeSuitability!.Rating);
	}

	public static IEnumerable<object[]> WireframeComparisonScenarioPaths() =>
		AiUnitReplWireframeComparisonScenarioCatalog.ScenarioPaths.Select(path => new object[] { path });
}

internal static class AiUnitReplWireframeComparisonOptions
{
	public static bool Enabled =>
		string.Equals(
			Environment.GetEnvironmentVariable("AIUNIT_REPL_VISUAL_COMPARISON_ENABLED"),
			"true",
			StringComparison.OrdinalIgnoreCase)
		|| string.Equals(
			Environment.GetEnvironmentVariable("AIUNIT_REPL_VISUAL_COMPARISON_ENABLED"),
			"1",
			StringComparison.Ordinal);
}

internal static class AiUnitReplWireframeComparisonJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public static JsonSerializerOptions IndentedOptions { get; } = new()
	{
		WriteIndented = true,
	};
}

internal static class AiUnitReplWireframeComparisonScenarioCatalog
{
	public static readonly string[] ScenarioPaths =
	[
		"tests/SharpNinja.AiUnit.Tests/AiUnitReplWireframeComparisons/01-workspace-overview.yaml",
		"tests/SharpNinja.AiUnit.Tests/AiUnitReplWireframeComparisons/02-project-strategy-editor.yaml",
		"tests/SharpNinja.AiUnit.Tests/AiUnitReplWireframeComparisons/03-strategy-catalog.yaml",
		"tests/SharpNinja.AiUnit.Tests/AiUnitReplWireframeComparisons/04-validation-deploy.yaml",
	];

	private static readonly IDeserializer Deserializer = new DeserializerBuilder()
		.WithNamingConvention(CamelCaseNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	public static string RepositoryRoot => FindRepositoryRoot();

	public static IReadOnlyList<AiUnitReplWireframeComparisonScenario> LoadAll() =>
		ScenarioPaths.Select(Load).ToArray();

	public static AiUnitReplWireframeComparisonScenario Load(string scenarioPath)
	{
		var fullPath = ResolveRepositoryPath(scenarioPath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException($"Could not find aiUnit REPL wireframe comparison scenario {scenarioPath}.", fullPath);
		}

		return LoadFromYaml(File.ReadAllText(fullPath), scenarioPath);
	}

	public static string ResolveRepositoryPath(string path)
	{
		if (Path.IsPathFullyQualified(path))
		{
			return path;
		}

		return Path.Combine(RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));
	}

	private static AiUnitReplWireframeComparisonScenario LoadFromYaml(string yaml, string sourcePath)
	{
		var scenario = Deserializer.Deserialize<AiUnitReplWireframeComparisonScenario>(yaml)
			?? throw new InvalidDataException($"Wireframe comparison scenario {sourcePath} did not deserialize.");
		scenario.SourcePath = sourcePath;
		scenario.RawYaml = yaml;
		scenario.ActualScreenshotFullPath = ResolveRepositoryPath(scenario.ActualScreenshotPath);
		scenario.WireframeScreenshotFullPath = ResolveRepositoryPath(scenario.WireframeScreenshotPath);
		scenario.WireframeSvgFullPath = ResolveRepositoryPath(scenario.WireframeSvgPath);
		scenario.AgentsReadmeFirstFullPath = ResolveRepositoryPath(scenario.AgentsReadmeFirstPath);
		scenario.AgentsReadmeFirstContent = File.Exists(scenario.AgentsReadmeFirstFullPath)
			? SanitizeAgentsContent(File.ReadAllText(scenario.AgentsReadmeFirstFullPath))
			: "AGENTS-README-FIRST.yaml was not present in this workspace. MCP marker content is unavailable; validate using the scenario traceability fields only.";

		Validate(scenario);
		scenario.ModelPayloadYaml = AppendAgentsReadmeFirstNode(scenario.RawYaml, scenario.AgentsReadmeFirstContent);
		return scenario;
	}

	private static void Validate(AiUnitReplWireframeComparisonScenario scenario)
	{
		RequireText(scenario.ScreenId, "screenId");
		RequireText(scenario.Title, "title");
		RequireText(scenario.Prompt, "prompt");
		RequireText(scenario.ActualScreenshotPath, "actualScreenshotPath");
		RequireText(scenario.WireframeScreenshotPath, "wireframeScreenshotPath");
		RequireText(scenario.WireframeSvgPath, "wireframeSvgPath");
		RequireText(scenario.ResultSchema, "resultSchema");
		RequireText(scenario.AgentsReadmeFirstPath, "agentsReadmeFirstPath");
		Require(scenario.FunctionalRequirements.Count > 0, "functionalRequirements must contain at least one FR.");
		Require(scenario.TechnicalRequirements.Count > 0, "technicalRequirements must contain at least one TR.");
		Require(scenario.FunctionalRequirements.All(item => item.Id.StartsWith("FR-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(item.Text)),
			"functionalRequirements must list explicit FR ids and text.");
		Require(scenario.TechnicalRequirements.All(item => item.Id.StartsWith("TR-", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(item.Text)),
			"technicalRequirements must list explicit TR ids and text.");
		Require(File.Exists(scenario.ActualScreenshotFullPath), $"actualScreenshotPath does not exist: {scenario.ActualScreenshotPath}");
		Require(File.Exists(scenario.WireframeScreenshotFullPath), $"wireframeScreenshotPath does not exist: {scenario.WireframeScreenshotPath}");
		Require(File.Exists(scenario.WireframeSvgFullPath), $"wireframeSvgPath does not exist: {scenario.WireframeSvgPath}");
		RequireText(scenario.AgentsReadmeFirstContent, "agentsReadmeFirstContent");
		Require(!scenario.AgentsReadmeFirstContent.Contains("apiKey:", StringComparison.OrdinalIgnoreCase),
			"agentsReadmeFirstContent must redact apiKey.");
		Require(!scenario.AgentsReadmeFirstContent.Contains("X-Api-Key", StringComparison.OrdinalIgnoreCase),
			"agentsReadmeFirstContent must redact X-Api-Key references.");
		Require(!scenario.AgentsReadmeFirstContent.Contains("api_key=", StringComparison.OrdinalIgnoreCase),
			"agentsReadmeFirstContent must redact api_key query references.");
		Require(scenario.ResultSchema.Contains("colorAccessibilityValidation", StringComparison.Ordinal),
			"resultSchema must require colorAccessibilityValidation.");
		using JsonDocument _ = JsonDocument.Parse(scenario.ResultSchema);
	}

	private static string AppendAgentsReadmeFirstNode(string rawYaml, string agentsReadmeFirstContent)
	{
		var builder = new StringBuilder();
		builder.Append(rawYaml.TrimEnd());
		builder.AppendLine();
		builder.AppendLine("agentsReadmeFirstContent: |");
		foreach (var line in agentsReadmeFirstContent.Replace("\r\n", "\n").Split('\n'))
		{
			builder.Append("  ");
			builder.AppendLine(line);
		}

		return builder.ToString();
	}

	private static string SanitizeAgentsContent(string content)
	{
		var builder = new StringBuilder();
		foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
		{
			var trimmed = line.TrimStart();
			if (ContainsCredentialReference(trimmed))
			{
				builder.Append(line[..(line.Length - trimmed.Length)]);
				builder.AppendLine("[redacted credential line]");
			}
			else
			{
				builder.AppendLine(line);
			}
		}

		return builder.ToString().TrimEnd();
	}

	private static bool ContainsCredentialReference(string line)
	{
		var lower = line.ToLowerInvariant();
		return lower.Contains("apikey", StringComparison.Ordinal)
			|| lower.Contains("api-key", StringComparison.Ordinal)
			|| lower.Contains("api_key", StringComparison.Ordinal)
			|| lower.Contains("x-api-key", StringComparison.Ordinal)
			|| lower.Contains("authorization", StringComparison.Ordinal)
			|| lower.Contains("bearer ", StringComparison.Ordinal)
			|| lower.Contains(" token", StringComparison.Ordinal)
			|| lower.StartsWith("token", StringComparison.Ordinal);
	}

	private static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidDataException(message);
		}
	}

	private static void RequireText(string? value, string fieldName) =>
		Require(!string.IsNullOrWhiteSpace(value), $"{fieldName} is required.");

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "SharpNinja.aiUnit.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException("Could not locate the aiUnit repository root.");
	}
}

internal sealed class AiUnitReplWireframeComparisonScenario
{
	public string SourcePath { get; set; } = string.Empty;

	public string RawYaml { get; set; } = string.Empty;

	public string ModelPayloadYaml { get; set; } = string.Empty;

	public string ScreenId { get; set; } = string.Empty;

	public string WireframeBaselineVersion { get; set; } = string.Empty;

	public string Title { get; set; } = string.Empty;

	public string Prompt { get; set; } = string.Empty;

	public List<RequirementReference> FunctionalRequirements { get; set; } = [];

	public List<RequirementReference> TechnicalRequirements { get; set; } = [];

	public string AgentsReadmeFirstPath { get; set; } = string.Empty;

	public string AgentsReadmeFirstFullPath { get; set; } = string.Empty;

	public string AgentsReadmeFirstContent { get; set; } = string.Empty;

	public string ActualScreenshotPath { get; set; } = string.Empty;

	public string ActualScreenshotFullPath { get; set; } = string.Empty;

	public string WireframeScreenshotPath { get; set; } = string.Empty;

	public string WireframeScreenshotFullPath { get; set; } = string.Empty;

	public string WireframeSvgPath { get; set; } = string.Empty;

	public string WireframeSvgFullPath { get; set; } = string.Empty;

	[YamlMember(ScalarStyle = ScalarStyle.Literal)]
	public string ResultSchema { get; set; } = string.Empty;
}

internal sealed class RequirementReference
{
	public string Id { get; set; } = string.Empty;

	public string Text { get; set; } = string.Empty;
}

internal static class AiUnitReplWireframeComparisonPrompt
{
	public static string BuildInstructions()
	{
		return """
		You are the aiUnit REPL TUI wireframe auditor.
		Compare the rendered wireframe baseline image against the actual finished TUI screenshot using the YAML screen comparison scenario file content.
		Return only valid JSON.
		Return exactly one valid JSON object matching the resultSchema YAML node. Do not wrap the response in Markdown. Do not add prose before or after the JSON.
		Do not omit any required section: screenId, summary, correctElements, incorrectElements, colorAccessibilityValidation, and wireframeSuitability are all required.
		Use the exact screenId provided by the YAML scenario.
		Use the functionalRequirements and technicalRequirements YAML nodes as the traceable FR/TR basis for the audit.
		Use the agentsReadmeFirstPath and appended agentsReadmeFirstContent YAML nodes to validate repository process requirements when they are relevant.
		correctElements must list material UI elements that appear correct in the actual screenshot compared with the wireframe and requirements.
		incorrectElements is only for actionable actual-TUI defects: missing required labels, missing required commands, wrong selected navigation item, unusable or clipped text, missing state indicators, unreadable wrapping, wrong containment, or colors that visibly violate a stated palette/accessibility requirement.
		Do not put wireframe-only defects in incorrectElements. When the actual screenshot satisfies the screen requirements but the wireframe is stale, simplified, or missing details, record that only in wireframeSuitability.missingRequirements and wireframeSuitability.recommendedWireframeChanges.
		Terminal rendering may be more compact than the SVG layout. Treat terminal table borders, monochrome text, lack of right-side panels, and compact command rows as acceptable when the same required screen regions, labels, states, and commands remain visible.
		Text differences, richer fixture values, and bounds differences are incorrectElements only when they contradict an explicit FR/TR/scenario requirement or materially change the screen task. Use an empty array when no actionable actual-TUI defect is visible.
		colorAccessibilityValidation must compare the wireframe and actual screenshot for palette consistency: backgrounds, borders, primary text, muted text, accent/status text, warning/error text, and selection/highlight affordances.
		Do not report speculative color or ADA issues. Only mark adaCompliant=false or add a deviation when the screenshots visibly show unreadable text, materially different required colors, or a control/status affordance that depends on color alone without text, shape, border, label, or position.
		colorsMatchWireframe means the actual TUI satisfies the required palette semantics, not pixel-perfect color equality. A monochrome terminal screenshot can satisfy color requirements when the labels, state words, borders, and contrast preserve the semantic affordances.
		colorAccessibilityValidation.deviations must be an empty array only when there are no visible color or ADA deviations. If colorsMatchWireframe or adaCompliant is false, deviations must explain why.
		wireframeSuitability must analyze whether the SVG wireframe itself is suitable for validating the screen requirements. It must include missingRequirements and recommendedWireframeChanges arrays, even when they are empty.
		A wireframe can be suitable when it shows the required screen region, major panels, labels, controls, state affordances, and command roles even if it intentionally omits exact terminal output formatting.
		wireframeSuitability.rating must be suitable unless the wireframe lacks an explicit FR/TR-required element, control, state, or interaction role needed to validate the screen.
		Use confidence values from 0 to 1.
		Use exactly one of these lowercase severity values: low, medium, high, critical.
		Use exactly one of these lowercase rating values: suitable, partially_suitable, unsuitable.
		The first image is the wireframe baseline rendered from SVG.
		The second image is the actual finished TUI screenshot.
		""";
	}

	public static string BuildUserPrompt(AiUnitReplWireframeComparisonScenario scenario)
	{
		return $"""
		YAML screen comparison scenario file content:
		```yaml
		{scenario.ModelPayloadYaml}
		```

		Image attachment order:
		1. wireframe baseline image: {scenario.WireframeScreenshotPath}
		2. actual finished TUI screenshot: {scenario.ActualScreenshotPath}

		Return exactly one JSON object that conforms to resultSchema.
		""";
	}

	public static IReadOnlyList<FrontierAttachment> BuildAttachments(AiUnitReplWireframeComparisonScenario scenario) =>
	[
		new("image/png", Path.GetFileName(scenario.WireframeScreenshotFullPath), File.ReadAllBytes(scenario.WireframeScreenshotFullPath)),
		new("image/png", Path.GetFileName(scenario.ActualScreenshotFullPath), File.ReadAllBytes(scenario.ActualScreenshotFullPath)),
		new("text/yaml", Path.GetFileName(scenario.SourcePath), Encoding.UTF8.GetBytes(scenario.ModelPayloadYaml)),
	];
}

internal static class AiUnitReplWireframeComparisonValidator
{
	private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
	{
		"screenId",
		"summary",
		"correctElements",
		"incorrectElements",
		"colorAccessibilityValidation",
		"wireframeSuitability",
	};

	private static readonly HashSet<string> ElementKeys = new(StringComparer.Ordinal)
	{
		"element",
		"requirement",
		"evidence",
		"confidence",
	};

	private static readonly HashSet<string> IncorrectElementKeys = new(StringComparer.Ordinal)
	{
		"element",
		"requirement",
		"expected",
		"actual",
		"whyIncorrect",
		"severity",
		"confidence",
	};

	private static readonly HashSet<string> ColorKeys = new(StringComparer.Ordinal)
	{
		"colorsMatchWireframe",
		"adaCompliant",
		"deviations",
		"why",
	};

	private static readonly HashSet<string> DeviationKeys = new(StringComparer.Ordinal)
	{
		"element",
		"expected",
		"actual",
		"whyDeviationMatters",
		"severity",
		"confidence",
	};

	private static readonly HashSet<string> SuitabilityKeys = new(StringComparer.Ordinal)
	{
		"rating",
		"why",
		"missingRequirements",
		"recommendedWireframeChanges",
	};

	private static readonly HashSet<string> Severities = new(StringComparer.Ordinal)
	{
		"low",
		"medium",
		"high",
		"critical",
	};

	private static readonly HashSet<string> Ratings = new(StringComparer.Ordinal)
	{
		"suitable",
		"partially_suitable",
		"unsuitable",
	};

	public static AiUnitReplWireframeComparisonResult ParseAndValidate(string json, string expectedScreenId)
	{
		using var document = JsonDocument.Parse(json);
		var root = document.RootElement;
		Require(root.ValueKind == JsonValueKind.Object, "Root response must be a JSON object.");
		RequireAllowedProperties(root, RootKeys, "root");
		RequireRequired(root, "root", RootKeys.ToArray());

		var screenId = RequireString(root, "screenId", "root");
		Require(string.Equals(screenId, expectedScreenId, StringComparison.Ordinal), $"screenId must equal '{expectedScreenId}'.");
		var summary = RequireString(root, "summary", "root");

		var correctElements = ValidateElementArray(root.GetProperty("correctElements"), "correctElements", ElementKeys, requireOne: true);
		var incorrectElements = ValidateElementArray(root.GetProperty("incorrectElements"), "incorrectElements", IncorrectElementKeys, requireOne: false);
		foreach (var incorrect in incorrectElements)
		{
			Require(Severities.Contains(incorrect.Severity ?? string.Empty), "incorrectElements severity is invalid.");
		}

		var color = ValidateColor(root.GetProperty("colorAccessibilityValidation"));
		var suitability = ValidateSuitability(root.GetProperty("wireframeSuitability"));

		return new AiUnitReplWireframeComparisonResult(
			screenId,
			summary,
			correctElements,
			incorrectElements,
			color,
			suitability);
	}

	private static List<AiUnitReplWireframeComparisonElement> ValidateElementArray(
		JsonElement array,
		string path,
		HashSet<string> allowedKeys,
		bool requireOne)
	{
		Require(array.ValueKind == JsonValueKind.Array, $"{path} must be an array.");
		if (requireOne)
		{
			Require(array.GetArrayLength() > 0, $"{path} must contain at least one item.");
		}

		var results = new List<AiUnitReplWireframeComparisonElement>();
		var index = 0;
		foreach (var item in array.EnumerateArray())
		{
			var itemPath = $"{path}[{index}]";
			Require(item.ValueKind == JsonValueKind.Object, $"{itemPath} must be an object.");
			RequireAllowedProperties(item, allowedKeys, itemPath);
			RequireRequired(item, itemPath, allowedKeys.ToArray());
			var element = RequireString(item, "element", itemPath);
			var requirement = RequireString(item, "requirement", itemPath);
			var evidence = item.TryGetProperty("evidence", out var evidenceProperty)
				? RequireString(item, "evidence", itemPath)
				: null;
			var expected = item.TryGetProperty("expected", out _)
				? RequireString(item, "expected", itemPath)
				: null;
			var actual = item.TryGetProperty("actual", out _)
				? RequireString(item, "actual", itemPath)
				: null;
			var whyIncorrect = item.TryGetProperty("whyIncorrect", out _)
				? RequireString(item, "whyIncorrect", itemPath)
				: null;
			var severity = item.TryGetProperty("severity", out _)
				? RequireString(item, "severity", itemPath)
				: null;
			var confidence = RequireNumber(item, "confidence", itemPath);
			results.Add(new AiUnitReplWireframeComparisonElement(
				element,
				requirement,
				evidence,
				expected,
				actual,
				whyIncorrect,
				severity,
				confidence));
			index++;
		}

		return results;
	}

	private static AiUnitReplWireframeColorValidation ValidateColor(JsonElement color)
	{
		Require(color.ValueKind == JsonValueKind.Object, "colorAccessibilityValidation must be an object.");
		RequireAllowedProperties(color, ColorKeys, "colorAccessibilityValidation");
		RequireRequired(color, "colorAccessibilityValidation", ColorKeys.ToArray());
		var colorsMatch = RequireBool(color, "colorsMatchWireframe", "colorAccessibilityValidation");
		var adaCompliant = RequireBool(color, "adaCompliant", "colorAccessibilityValidation");
		var deviations = color.GetProperty("deviations");
		Require(deviations.ValueKind == JsonValueKind.Array, "colorAccessibilityValidation.deviations must be an array.");
		if (!colorsMatch || !adaCompliant)
		{
			Require(
				deviations.GetArrayLength() > 0,
				"colorAccessibilityValidation.deviations must explain failed color or ADA validation.");
		}

		var parsed = new List<AiUnitReplWireframeColorDeviation>();
		var index = 0;
		foreach (var deviation in deviations.EnumerateArray())
		{
			var path = $"colorAccessibilityValidation.deviations[{index}]";
			Require(deviation.ValueKind == JsonValueKind.Object, $"{path} must be an object.");
			RequireAllowedProperties(deviation, DeviationKeys, path);
			RequireRequired(deviation, path, DeviationKeys.ToArray());
			var severity = RequireString(deviation, "severity", path);
			Require(Severities.Contains(severity), $"{path}.severity is invalid.");
			parsed.Add(new AiUnitReplWireframeColorDeviation(
				RequireString(deviation, "element", path),
				RequireString(deviation, "expected", path),
				RequireString(deviation, "actual", path),
				RequireString(deviation, "whyDeviationMatters", path),
				severity,
				RequireNumber(deviation, "confidence", path)));
			index++;
		}

		return new AiUnitReplWireframeColorValidation(
			colorsMatch,
			adaCompliant,
			parsed,
			RequireString(color, "why", "colorAccessibilityValidation"));
	}

	private static AiUnitReplWireframeSuitability ValidateSuitability(JsonElement suitability)
	{
		Require(suitability.ValueKind == JsonValueKind.Object, "wireframeSuitability must be an object.");
		RequireAllowedProperties(suitability, SuitabilityKeys, "wireframeSuitability");
		RequireRequired(suitability, "wireframeSuitability", SuitabilityKeys.ToArray());
		var rating = RequireString(suitability, "rating", "wireframeSuitability");
		Require(Ratings.Contains(rating), "wireframeSuitability.rating is invalid.");
		return new AiUnitReplWireframeSuitability(
			rating,
			RequireString(suitability, "why", "wireframeSuitability"),
			RequireStringArray(suitability, "missingRequirements", "wireframeSuitability"),
			RequireStringArray(suitability, "recommendedWireframeChanges", "wireframeSuitability"));
	}

	private static void RequireAllowedProperties(JsonElement element, HashSet<string> allowed, string path)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (!allowed.Contains(property.Name))
			{
				throw new InvalidDataException($"Unexpected {path} property '{property.Name}'.");
			}
		}
	}

	private static void RequireRequired(JsonElement element, string path, params string[] keys)
	{
		foreach (var key in keys)
		{
			Require(element.TryGetProperty(key, out _), $"{path}.{key} is required.");
		}
	}

	private static string RequireString(JsonElement element, string name, string path)
	{
		Require(element.TryGetProperty(name, out var property), $"{path}.{name} is required.");
		Require(property.ValueKind == JsonValueKind.String, $"{path}.{name} must be a string.");
		var value = property.GetString() ?? string.Empty;
		Require(!string.IsNullOrWhiteSpace(value), $"{path}.{name} must not be empty.");
		return value;
	}

	private static bool RequireBool(JsonElement element, string name, string path)
	{
		Require(element.TryGetProperty(name, out var property), $"{path}.{name} is required.");
		Require(property.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{path}.{name} must be a boolean.");
		return property.GetBoolean();
	}

	private static double RequireNumber(JsonElement element, string name, string path)
	{
		Require(element.TryGetProperty(name, out var property), $"{path}.{name} is required.");
		Require(property.ValueKind == JsonValueKind.Number, $"{path}.{name} must be a number.");
		var value = property.GetDouble();
		Require(value is >= 0 and <= 1, $"{path}.{name} must be between 0 and 1.");
		return value;
	}

	private static IReadOnlyList<string> RequireStringArray(JsonElement element, string name, string path)
	{
		Require(element.TryGetProperty(name, out var property), $"{path}.{name} is required.");
		Require(property.ValueKind == JsonValueKind.Array, $"{path}.{name} must be an array.");
		var values = new List<string>();
		var index = 0;
		foreach (var item in property.EnumerateArray())
		{
			Require(item.ValueKind == JsonValueKind.String, $"{path}.{name}[{index}] must be a string.");
			values.Add(item.GetString() ?? string.Empty);
			index++;
		}

		return values;
	}

	private static void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidDataException(message);
		}
	}
}

internal sealed record AiUnitReplWireframeComparisonResult(
	string ScreenId,
	string Summary,
	IReadOnlyList<AiUnitReplWireframeComparisonElement> CorrectElements,
	IReadOnlyList<AiUnitReplWireframeComparisonElement> IncorrectElements,
	AiUnitReplWireframeColorValidation? ColorAccessibilityValidation,
	AiUnitReplWireframeSuitability? WireframeSuitability);

internal sealed record AiUnitReplWireframeComparisonElement(
	string Element,
	string Requirement,
	string? Evidence,
	string? Expected,
	string? Actual,
	string? WhyIncorrect,
	string? Severity,
	double Confidence);

internal sealed record AiUnitReplWireframeColorValidation(
	bool ColorsMatchWireframe,
	bool AdaCompliant,
	IReadOnlyList<AiUnitReplWireframeColorDeviation> Deviations,
	string Why);

internal sealed record AiUnitReplWireframeColorDeviation(
	string Element,
	string Expected,
	string Actual,
	string WhyDeviationMatters,
	string Severity,
	double Confidence);

internal sealed record AiUnitReplWireframeSuitability(
	string Rating,
	string Why,
	IReadOnlyList<string> MissingRequirements,
	IReadOnlyList<string> RecommendedWireframeChanges);

internal static class PngProbe
{
	public static (int Width, int Height) ReadDimensions(string path)
	{
		var bytes = File.ReadAllBytes(path);
		if (bytes.Length < 24)
		{
			throw new InvalidDataException($"PNG is too small: {path}");
		}

		return (ReadBigEndianInt32(bytes, 16), ReadBigEndianInt32(bytes, 20));
	}

	private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
		(bytes[offset] << 24)
		| (bytes[offset + 1] << 16)
		| (bytes[offset + 2] << 8)
		| bytes[offset + 3];
}
