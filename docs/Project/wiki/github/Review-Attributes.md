# Review Attributes

aiUnit includes three stackable xUnit data attributes for AI review workflows:

- `[CodeReview]`
- `[PlanReview]`
- `[ProjectReview]`

Each attribute accepts one optional string prompt. If the prompt is null or
empty, aiUnit loads the canned YAML prompt for that review scope:

- `Review/Prompts/code-review.yaml`
- `Review/Prompts/plan-review.yaml`
- `Review/Prompts/project-review.yaml`

The decorated method receives the effective prompt as the first parameter and
the review result JSON as the second parameter.

```csharp
[Theory]
[CodeReview("Review this serializer for correctness.", Agent = "codex-subscription")]
public void ReviewSerializer(string prompt, string resultJson)
{
    AiUnitJsonAssertions.AssertValidJson(resultJson);
}
```

## Agent Selection

Use `Agent` for one named strategy or `Agents` for multiple named strategies.
Inline agent details can also be supplied:

- `Kind`
- `BaseUrl`
- `Model`
- `ApiKeyEnvVar`
- `Command`
- `TimeoutSeconds`
- `Temperature`
- `MaxTokens`

When multiple agents run for one review, aiUnit uses the default active strategy
to aggregate the individual reviews into one result JSON payload.

## Result Schema

The canned prompts include the aiUnit review-findings JSON schema, and aiUnit
also sends that schema as a structured tool definition when the configured
frontier client supports tools. Review methods should treat `resultJson` as
read-only evidence from the configured agent run.

Process `resultJson` as immutable review evidence:

1. Parse it as JSON and verify `schemaVersion` is
   `aiunit.review.findings.v1`.
2. Read `status`: `pass` means no blocking findings, `fail` means the review
   found actionable issues, and `error` means the review could not complete
   cleanly.
3. Inspect `summary`, `reviewedScope`, and `findings` for assertion details.
4. Treat `agentReviews` as supporting evidence from individual agents when the
   review used multiple agents; the top-level object is the aggregated result.

```csharp
[Theory]
[ProjectReview(Agent = "codex-subscription")]
public void ProjectReviewMustPass(string prompt, string resultJson)
{
    using var doc = JsonDocument.Parse(resultJson);
    var root = doc.RootElement;

    Assert.Equal("aiunit.review.findings.v1", root.GetProperty("schemaVersion").GetString());
    Assert.NotEqual("error", root.GetProperty("status").GetString());

    var blocking = root
        .GetProperty("findings")
        .EnumerateArray()
        .Where(f => f.GetProperty("severity").GetString() is "critical" or "high")
        .Select(f => f.GetProperty("title").GetString())
        .ToArray();

    Assert.Empty(blocking);
}
```

Example `resultJson`:

```json
{
  "schemaVersion": "aiunit.review.findings.v1",
  "reviewType": "code",
  "status": "fail",
  "summary": "One high-severity issue was found in the serializer changes.",
  "reviewedScope": "src/SharpNinja.AiUnit/Serialization",
  "agent": {
    "name": "codex-subscription",
    "provider": "codex-subscription:codex",
    "model": "(cli-managed)"
  },
  "findings": [
    {
      "severity": "high",
      "category": "correctness",
      "title": "Missing null handling for optional payload",
      "detail": "The serializer reads the optional payload without checking JsonValueKind.Null, so a valid null value throws instead of producing an empty result.",
      "recommendation": "Handle JsonValueKind.Null before reading object properties and add a regression test for null payloads.",
      "filePath": "src/SharpNinja.AiUnit/Serialization/ReviewResultReader.cs",
      "line": 42,
      "ruleId": "AIUNIT-CODE-NULL-001",
      "confidence": 0.86,
      "agent": "codex-subscription"
    }
  ]
}
```
