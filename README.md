# SharpNinja.aiUnit

xUnit extension for AI frontier-model regression testing, AI-assisted review tests,
and workspace strategy management.

`aiUnit` provides:

- HTTP frontier adapters for Anthropic Claude, OpenAI-compatible APIs
  (OpenAI, xAI, MAF, and similar gateways), and Google Gemini.
- CLI strategies for local executable-backed agents such as Claude Code and
  OpenAI Codex.
- Strategy resolution from `appsettings.aiunit.json` with `AIUNIT_*`
  environment-variable overrides.
- `[AiFact]` and `[AiTheory]` xUnit attributes that auto-skip when no configured
  strategy can run.
- `[CodeReview]`, `[PlanReview]`, and `[ProjectReview]` xUnit data attributes
  that run AI reviews and pass the effective prompt plus result JSON to the
  decorated method.
- `AiUnitJsonAssertions` for validating model JSON output.
- `aiunit` REPL/TUI commands for scanning workspaces, listing projects,
  inspecting strategy catalogs, applying strategy config, restoring snapshots,
  and validating configuration.

## Strategy Configuration

Add `appsettings.aiunit.json` to the test project output directory. The sample
file in `samples/appsettings.aiunit.json` contains ready-to-edit examples.

Strategy kinds:

- `cli`: launches the configured executable from `Command`. The executable owns
  authentication. `ApiKeyEnvVar` is not used for `cli` strategies.
- `anthropic`: calls the Anthropic Messages API directly.
- `openai-compatible`: calls `/v1/chat/completions` on an OpenAI-compatible
  endpoint such as OpenAI, xAI, or MAF.
- `gemini`: calls the Google Gemini Generative Language API.

Environment overrides:

- `AIUNIT_STRATEGY`
- `AIUNIT_KIND`
- `AIUNIT_BASE_URL`
- `AIUNIT_MODEL`
- `AIUNIT_COMMAND`
- `AIUNIT_API_KEY`
- `AIUNIT_TIMEOUT_SECONDS`
- `AIUNIT_TEMPERATURE`

CLI-backed examples:

```json
{
  "AiUnit": {
    "ActiveStrategy": "codex-subscription",
    "Strategies": {
      "codex-subscription": {
        "Kind": "cli",
        "Command": "codex",
        "Model": "(cli-managed)",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Codex CLI using the logged-in subscription."
      },
      "claude-code-opus": {
        "Kind": "cli",
        "Command": "claude",
        "Model": "claude-opus-4-5",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Claude Code CLI using Opus. Configure ANTHROPIC_API_KEY or Claude CLI auth in the process environment."
      },
      "copilot-gemini": {
        "Kind": "cli",
        "Command": "copilot",
        "Model": "gemini-2.5-pro",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Copilot CLI configured for Gemini. Configure the required Copilot CLI auth or API key in the process environment."
      },
      "maf-grok": {
        "Kind": "openai-compatible",
        "BaseUrl": "https://api.x.ai",
        "Model": "grok-4",
        "ApiKeyEnvVar": "XAI_API_KEY",
        "TimeoutSeconds": 1800,
        "Temperature": 0.0,
        "Description": "xAI Grok through the OpenAI-compatible API. Set XAI_API_KEY before activating."
      }
    }
  }
}
```

For CLI strategies, configure the launched tool before running tests:

```powershell
# Codex CLI reads the logged-in subscription.
codex login

# Claude Code can read Anthropic auth from the shell environment.
$env:ANTHROPIC_API_KEY = "<anthropic-api-key>"

# Copilot CLI auth depends on the installed executable.
$env:COPILOT_API_KEY = "<copilot-api-key>"
```

For HTTP strategies, set the configured API-key variable or use
`AIUNIT_API_KEY` as a shared fallback.

## Review Attributes

Use review attributes on xUnit theory methods. Each decorated method receives
two read-only parameters:

```csharp
[Theory]
[CodeReview("Review this serializer for correctness.", Agent = "codex-subscription")]
public void ReviewSerializer(string prompt, string resultJson)
{
    AiUnitJsonAssertions.AssertValidJson(resultJson);
}
```

Available review attributes:

- `[CodeReview]`
- `[PlanReview]`
- `[ProjectReview]`

If the prompt argument is null or empty, aiUnit loads the canned YAML prompt for
that review scope:

- `Review/Prompts/code-review.yaml`
- `Review/Prompts/plan-review.yaml`
- `Review/Prompts/project-review.yaml`

The canned prompts include the JSON schema that agents must use for review
findings. The same schema is also sent as a structured tool definition when the
configured frontier client supports tools.

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

Agent configuration can come from a named strategy (`Agent` or `Agents`) or
inline attribute properties: `Kind`, `BaseUrl`, `Model`, `ApiKeyEnvVar`,
`Command`, `TimeoutSeconds`, `Temperature`, and `MaxTokens`. Attributes are
stackable. When multiple agents run for one review, the default active strategy
aggregates their findings into one result JSON payload.

## CLI Usage

```powershell
aiunit repl --workspace F:\GitHub\aiUnit
aiunit tui overview --workspace F:\GitHub\aiUnit
aiunit scan --workspace F:\GitHub\aiUnit
aiunit list --workspace F:\GitHub\aiUnit
aiunit catalog --workspace F:\GitHub\aiUnit
aiunit validate --workspace F:\GitHub\aiUnit
aiunit show <project> --workspace F:\GitHub\aiUnit
aiunit apply <strategy> --project <project> --dry-run --force --workspace F:\GitHub\aiUnit
aiunit apply-global <strategy> --dry-run --force --workspace F:\GitHub\aiUnit
aiunit restore <project> --snapshot <path> --workspace F:\GitHub\aiUnit
aiunit --version
aiunit --help
```

## Validation

```powershell
dotnet test
dotnet run --project src/SharpNinja.AiUnit.Repl -- --help
```

## License

MIT. See [LICENSE](LICENSE).
