using System;
using System.Text.Json;
using SharpNinja.AiUnit.Review;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

public sealed class AiReviewDogfoodTests
{
	private const string MissingReviewAgent = "__aiunit_dogfood_missing_agent__";

	[Theory]
	[CodeReview("Dogfood code review: review the AiReview attribute data-row contract.", Agent = MissingReviewAgent)]
	[PlanReview("Dogfood plan review: review the PLAN-AIUNITREPL-001 validation gates.", Agent = MissingReviewAgent)]
	[ProjectReview("Dogfood project review: review aiUnit package readiness.", Agent = MissingReviewAgent)]
	public void ReviewAttributes_DogfoodCustomPrompts_PassPromptAndResultJsonToDecoratedMethod(
		string prompt,
		string resultJson)
	{
		using var doc = JsonDocument.Parse(resultJson);
		var root = doc.RootElement;
		var reviewType = RequiredString(root, "reviewType");

		Assert.Equal(AiReviewFindingsSchema.SchemaVersion, RequiredString(root, "schemaVersion"));
		Assert.Equal("error", RequiredString(root, "status"));
		Assert.Contains(MissingReviewAgent, RequiredString(root, "summary"), StringComparison.Ordinal);
		Assert.Equal(MissingReviewAgent, RequiredString(root.GetProperty("agent"), "name"));
		Assert.True(root.TryGetProperty("findings", out var findings));
		Assert.Equal(JsonValueKind.Array, findings.ValueKind);
		Assert.NotEmpty(findings.EnumerateArray());

		switch (reviewType)
		{
			case "code":
				Assert.Contains("Dogfood code review", prompt, StringComparison.Ordinal);
				break;
			case "plan":
				Assert.Contains("Dogfood plan review", prompt, StringComparison.Ordinal);
				break;
			case "project":
				Assert.Contains("Dogfood project review", prompt, StringComparison.Ordinal);
				break;
			default:
				Assert.Fail($"Unexpected reviewType '{reviewType}'.");
				break;
		}
	}

	[Theory]
	[CodeReview(Agent = MissingReviewAgent)]
	[PlanReview(Agent = MissingReviewAgent)]
	[ProjectReview(Agent = MissingReviewAgent)]
	public void ReviewAttributes_DogfoodDefaultPrompts_UseScopeSpecificCannedPrompt(
		string prompt,
		string resultJson)
	{
		using var doc = JsonDocument.Parse(resultJson);
		var reviewType = RequiredString(doc.RootElement, "reviewType");

		Assert.Contains("Return only JSON matching this schema.", prompt, StringComparison.Ordinal);
		Assert.Contains(AiReviewFindingsSchema.SchemaVersion, prompt, StringComparison.Ordinal);
		Assert.Contains($"reviewedScope to \"{reviewType}\"", prompt, StringComparison.Ordinal);
		Assert.Equal(MissingReviewAgent, RequiredString(doc.RootElement.GetProperty("agent"), "name"));

		switch (reviewType)
		{
			case "code":
				Assert.Contains("Scope: code review.", prompt, StringComparison.Ordinal);
				Assert.Contains("behavioral regressions", prompt, StringComparison.OrdinalIgnoreCase);
				break;
			case "plan":
				Assert.Contains("Scope: plan review.", prompt, StringComparison.Ordinal);
				Assert.Contains("validation gates", prompt, StringComparison.OrdinalIgnoreCase);
				break;
			case "project":
				Assert.Contains("Scope: project review.", prompt, StringComparison.Ordinal);
				Assert.Contains("requirements coverage", prompt, StringComparison.OrdinalIgnoreCase);
				break;
			default:
				Assert.Fail($"Unexpected reviewType '{reviewType}'.");
				break;
		}
	}

	private static string RequiredString(JsonElement element, string propertyName)
	{
		Assert.True(element.TryGetProperty(propertyName, out var property), $"{propertyName} is required.");
		Assert.Equal(JsonValueKind.String, property.ValueKind);
		var value = property.GetString();
		Assert.False(string.IsNullOrWhiteSpace(value), $"{propertyName} must not be empty.");
		return value!;
	}
}
