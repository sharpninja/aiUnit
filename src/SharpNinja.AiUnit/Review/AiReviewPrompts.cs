using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SharpNinja.AiUnit.Review;

/// <summary>Default review prompt YAML files and prompt composition helpers.</summary>
public static class AiReviewPrompts
{
	private const string ResourcePrefix = "SharpNinja.AiUnit.Review.Prompts.";
	private const string JsonSchemaToken = "{{reviewFindingsJsonSchema}}";

	private static readonly Lazy<IReadOnlyDictionary<AiReviewKind, string>> DefaultPrompts = new(LoadDefaultPrompts);

	/// <summary>Returns the package-relative YAML file name for a built-in default review prompt.</summary>
	public static string DefaultPromptFileName(AiReviewKind kind) => kind switch
	{
		AiReviewKind.Code => "Review/Prompts/code-review.yaml",
		AiReviewKind.Plan => "Review/Prompts/plan-review.yaml",
		AiReviewKind.Project => "Review/Prompts/project-review.yaml",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	/// <summary>Returns the default prompt loaded from the built-in YAML file for a review kind.</summary>
	public static string CannedPrompt(AiReviewKind kind) => DefaultPrompts.Value[kind];

	/// <summary>Normalizes an attribute prompt, falling back to the YAML default prompt when needed.</summary>
	public static string EffectivePrompt(AiReviewKind kind, string? prompt) =>
		string.IsNullOrWhiteSpace(prompt) ? CannedPrompt(kind) : prompt.Trim();

	internal static string DefaultPromptResourceName(AiReviewKind kind) =>
		ResourcePrefix + Path.GetFileName(DefaultPromptFileName(kind));

	internal static string ParsePromptYaml(string yaml, string sourceName)
	{
		ArgumentNullException.ThrowIfNull(yaml);
		ArgumentNullException.ThrowIfNull(sourceName);

		var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			var trimmedStart = line.TrimStart();
			var keyIndent = line.Length - trimmedStart.Length;
			if (!trimmedStart.StartsWith("prompt:", StringComparison.Ordinal))
			{
				continue;
			}

			var value = trimmedStart["prompt:".Length..].Trim();
			if (value.StartsWith('|'))
			{
				return ParseLiteralBlock(lines, i + 1, keyIndent, sourceName);
			}

			if (value.Length > 0)
			{
				return Unquote(value).Trim();
			}

			throw new InvalidOperationException($"Default review prompt YAML '{sourceName}' has an empty prompt value.");
		}

		throw new InvalidOperationException($"Default review prompt YAML '{sourceName}' does not contain a prompt field.");
	}

	internal static string BuildSystemPrompt(AiReviewKind kind) =>
		$"""
		You are performing an aiUnit {ReviewTypeName(kind)} review.
		Return only JSON matching this schema. Do not wrap the JSON in markdown.
		Schema:
		{AiReviewFindingsSchema.JsonSchema}
		""";

	internal static string BuildUserPrompt(AiReviewKind kind, string prompt) =>
		$"""
		Review type: {ReviewTypeName(kind)}

		Prompt:
		{prompt}
		""";

	internal static string BuildAggregationPrompt(
		AiReviewKind kind,
		string prompt,
		IReadOnlyList<AiReviewAgentResult> reviews) =>
		$"""
		Aggregate the following aiUnit {ReviewTypeName(kind)} review results into one final result.
		Preserve all actionable findings, deduplicate overlaps, keep the highest justified severity, and return only JSON matching the schema.

		Original prompt:
		{prompt}

		Review results:
		{AiReviewJson.BuildAgentReviewsJson(reviews)}
		""";

	internal static string ReviewTypeName(AiReviewKind kind) => kind switch
	{
		AiReviewKind.Code => "code",
		AiReviewKind.Plan => "plan",
		AiReviewKind.Project => "project",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	private static IReadOnlyDictionary<AiReviewKind, string> LoadDefaultPrompts()
	{
		var prompts = new Dictionary<AiReviewKind, string>();
		foreach (var kind in Enum.GetValues<AiReviewKind>())
		{
			prompts[kind] = LoadDefaultPrompt(kind);
		}
		return prompts;
	}

	private static string LoadDefaultPrompt(AiReviewKind kind)
	{
		var resourceName = DefaultPromptResourceName(kind);
		var assembly = typeof(AiReviewPrompts).GetTypeInfo().Assembly;
		using var stream = assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Embedded default review prompt resource '{resourceName}' was not found.");
		using var reader = new StreamReader(stream);
		return ExpandPromptTokens(ParsePromptYaml(reader.ReadToEnd(), DefaultPromptFileName(kind)));
	}

	private static string ExpandPromptTokens(string prompt) =>
		prompt.Replace(JsonSchemaToken, AiReviewFindingsSchema.JsonSchema, StringComparison.Ordinal);

	private static string ParseLiteralBlock(string[] lines, int startIndex, int keyIndent, string sourceName)
	{
		var blockIndent = -1;
		var values = new List<string>();
		for (var i = startIndex; i < lines.Length; i++)
		{
			var line = lines[i];
			var trimmedStart = line.TrimStart();
			if (trimmedStart.Length == 0)
			{
				values.Add(string.Empty);
				continue;
			}

			var indent = line.Length - trimmedStart.Length;
			if (blockIndent < 0)
			{
				if (indent <= keyIndent)
				{
					break;
				}
				blockIndent = indent;
			}
			else if (indent < blockIndent)
			{
				break;
			}

			values.Add(line.Length >= blockIndent ? line[blockIndent..] : string.Empty);
		}

		var prompt = string.Join(Environment.NewLine, values).Trim();
		if (prompt.Length == 0)
		{
			throw new InvalidOperationException($"Default review prompt YAML '{sourceName}' has an empty prompt value.");
		}
		return prompt;
	}

	private static string Unquote(string value)
	{
		if (value.Length >= 2
			&& ((value[0] == '"' && value[^1] == '"')
				|| (value[0] == '\'' && value[^1] == '\'')))
		{
			return value[1..^1];
		}
		return value;
	}
}
