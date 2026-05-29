# SharpNinja.aiUnit

xUnit extension for frontier-model regression testing, AI-assisted code review, and
workspace strategy management. Write tests that call real AI models, auto-skip when
no model is configured, and tolerate transient failures through a built-in resilience
pipeline.

[![NuGet](https://img.shields.io/nuget/v/SharpNinja.aiUnit.svg)](https://www.nuget.org/packages/SharpNinja.aiUnit)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## Contents

- [Quick Start](#quick-start)
- [Strategy Configuration](#strategy-configuration)
- [Writing AI Tests](#writing-ai-tests)
- [Resilience Pipeline](#resilience-pipeline)
- [Review Attributes](#review-attributes)
- [JSON Assertions](#json-assertions)
- [aiunit CLI Tool](#aiunit-cli-tool)
- [Advanced Usage](#advanced-usage)
- [License](#license)

---

## Quick Start

### 1. Install the NuGet package

```
dotnet add package SharpNinja.aiUnit
```

### 2. Add a strategy config file

Create `appsettings.aiunit.json` in the test project and set it to copy to the output
directory (`CopyToOutputDirectory: PreserveNewest`). Pick any strategy kind that
matches your setup:

```json
{
  "AiUnit": {
    "ActiveStrategy": "claude",
    "Strategies": {
      "claude": {
        "Kind": "cli",
        "Command": "claude",
        "Model": "claude-sonnet-4-6",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Claude Code CLI."
      }
    }
  }
}
```

### 3. Write your first test

```csharp
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Xunit;

public class MyAiTests
{
    [AiFact]
    public async Task Model_CanAnswerSimpleQuestion()
    {
        var client = AiStrategyFixture.Default.Client!;
        var response = await client.SendAsync(
            new FrontierRequest("You are a helpful assistant.", "What is 2 + 2?"));

        Assert.Null(response.Error);
        Assert.Contains("4", response.Text!);
    }
}
```

The `[AiFact]` attribute auto-skips the test if no strategy resolves, so your CI
pipeline never fails on machines without an API key configured.

```
dotnet test
```

---

## Strategy Configuration

### Config file

aiUnit looks for `appsettings.aiunit.json` starting from the test output directory and
walking up to parent directories. The file selects a strategy by name; the active
strategy is resolved at process start and shared by all tests in the run.

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
      "claude": {
        "Kind": "cli",
        "Command": "claude",
        "Model": "claude-sonnet-4-6",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Claude Code CLI."
      },
      "grok": {
        "Kind": "openai-compatible",
        "BaseUrl": "https://api.x.ai",
        "Model": "grok-4",
        "ApiKeyEnvVar": "XAI_API_KEY",
        "TimeoutSeconds": 1800,
        "Temperature": 0.0,
        "Description": "xAI Grok-4 via OpenAI-compatible API."
      },
      "gemini": {
        "Kind": "gemini",
        "BaseUrl": "https://generativelanguage.googleapis.com",
        "Model": "gemini-2.5-flash",
        "ApiKeyEnvVar": "GOOGLE_API_KEY",
        "TimeoutSeconds": 600,
        "Temperature": 0.0,
        "Description": "Google Gemini Generative Language API."
      },
      "claude-api": {
        "Kind": "anthropic",
        "BaseUrl": "https://api.anthropic.com",
        "Model": "claude-opus-4-5",
        "ApiKeyEnvVar": "ANTHROPIC_API_KEY",
        "TimeoutSeconds": 600,
        "Temperature": 0.0,
        "Description": "Anthropic HTTP API (no CLI required)."
      }
    }
  }
}
```

### Strategy kinds

| Kind | Description | Auth |
|------|-------------|------|
| `cli` | Spawns the configured `Command` executable and parses its output. | Handled by the CLI tool. `ApiKeyEnvVar` is ignored. |
| `anthropic` | Calls `/v1/messages` on the Anthropic API. | `ApiKeyEnvVar` or `AIUNIT_API_KEY`. |
| `openai-compatible` | Calls `/v1/chat/completions` on any OpenAI-compatible endpoint. | `ApiKeyEnvVar` or `AIUNIT_API_KEY`. |
| `gemini` | Calls the Google Generative Language API. | `ApiKeyEnvVar` or `AIUNIT_API_KEY`. |

### Environment-variable overrides

All config values can be overridden at run time:

| Variable | Overrides |
|----------|-----------|
| `AIUNIT_STRATEGY` | `ActiveStrategy` |
| `AIUNIT_KIND` | strategy `Kind` |
| `AIUNIT_BASE_URL` | strategy `BaseUrl` |
| `AIUNIT_MODEL` | strategy `Model` |
| `AIUNIT_COMMAND` | strategy `Command` |
| `AIUNIT_API_KEY` | strategy `ApiKeyEnvVar` value |
| `AIUNIT_TIMEOUT_SECONDS` | strategy `TimeoutSeconds` |
| `AIUNIT_TEMPERATURE` | strategy `Temperature` |

### CLI-strategy setup

Before running tests with a CLI strategy, authenticate the tool:

```powershell
# Claude Code
$env:ANTHROPIC_API_KEY = "<key>"
# or use: claude auth login

# Codex CLI
codex login

# Copilot CLI
$env:COPILOT_API_KEY = "<key>"
```

For HTTP strategies, set the variable named in `ApiKeyEnvVar`:

```powershell
$env:ANTHROPIC_API_KEY = "<key>"
$env:XAI_API_KEY       = "<key>"
$env:GOOGLE_API_KEY    = "<key>"
```

---

## Writing AI Tests

### `[AiFact]` and `[AiTheory]`

Both attributes extend their xUnit counterparts and add an auto-skip guard: if no
strategy resolves, the test is marked **skipped** (not failed) with a clear reason.

```csharp
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Xunit;

public class IntentExtractionTests
{
    [AiFact]
    public async Task Model_ExtractsIntentAsJson()
    {
        var client = AiStrategyFixture.Default.Client!;

        var response = await client.SendAsync(new FrontierRequest(
            SystemPrompt: "Extract user intent. Return JSON: {\"intent\": \"<value>\"}",
            UserMessage: "I want to book a flight to Seattle next Tuesday.",
            RequireJsonOutput: true));

        Assert.Null(response.Error);

        using var doc = System.Text.Json.JsonDocument.Parse(response.Text!);
        Assert.Equal("book_flight",
            doc.RootElement.GetProperty("intent").GetString());
    }

    [AiTheory]
    [InlineData("Book a flight to Paris", "book_flight")]
    [InlineData("Cancel my reservation #4821", "cancel_reservation")]
    public async Task Model_ClassifiesIntents(string input, string expectedIntent)
    {
        var client = AiStrategyFixture.Default.Client!;

        var response = await client.SendAsync(new FrontierRequest(
            SystemPrompt: "Return JSON: {\"intent\": \"<value>\"}",
            UserMessage: input,
            RequireJsonOutput: true));

        Assert.Null(response.Error);
        using var doc = System.Text.Json.JsonDocument.Parse(response.Text!);
        Assert.Equal(expectedIntent,
            doc.RootElement.GetProperty("intent").GetString());
    }
}
```

### Shared fixture

For xUnit class fixtures or collection fixtures, access the process-wide singleton
directly. Every test in the run shares the same resolved client:

```csharp
public class FixtureTests : IClassFixture<AiStrategyFixture>
{
    private readonly AiStrategyFixture _fixture;

    public FixtureTests(AiStrategyFixture _) =>
        _fixture = AiStrategyFixture.Default;

    [AiFact]
    public async Task Fixture_ClientResolves()
    {
        Assert.True(_fixture.IsResolved, _fixture.SkipReason);

        var response = await _fixture.Client!.SendAsync(
            new FrontierRequest("You are an echo bot.", "Echo: hello"));

        Assert.Null(response.Error);
    }
}
```

### FrontierRequest options

```csharp
new FrontierRequest(
    SystemPrompt:     "You are a strict JSON validator.",
    UserMessage:      "Validate this payload: ...",
    RequireJsonOutput: true,       // hint to the adapter to use JSON mode
    MaxTokens:        512,         // optional token cap
    Temperature:      0.1,         // optional override
    Attachments:      [            // optional vision attachments
        new FrontierAttachment("image/png", "screenshot.png", imageBytes)
    ]
)
```

### FrontierResponse

```csharp
FrontierResponse response = await client.SendAsync(request);

// Success path
string text      = response.Text!;             // model output
long   latencyMs = response.LatencyMs;         // wall-clock ms
int    input     = response.TokenUsage.InputTokens;
int    output    = response.TokenUsage.OutputTokens;

// Error path (never throws; always check)
if (response.Error is { } err)
{
    // err.ErrorCode: "auth" | "rate_limit" | "timeout" | "server_error"
    //               | "network" | "malformed_response" | "circuit_open"
    //               | "AttachmentTooLarge" | "unexpected"
    Skip.If(true, $"AI call failed: {err.ErrorCode} - {err.Message}");
}
```

---

## Resilience Pipeline

Every AI call is automatically wrapped in a configurable Polly v8 resilience pipeline.
The pipeline is always on by default with safe library defaults.

### Pipeline stages (outer to inner)

| Stage | Behavior |
|-------|----------|
| **Fallback** (optional) | When the circuit opens, routes the call to an alternate strategy instead of failing. Only active when `FallbackStrategy` is set. |
| **Circuit breaker** | Opens after `BreakAfterConsecutiveFailures` consecutive failures and stays open for `BreakDurationSeconds`. Prevents hammering a failing endpoint. |
| **Retry** | Retries `MaxRetries` times on transient failures with configurable backoff and jitter. |
| **Timeout** | Cancels each attempt after `TimeoutSeconds` seconds. Per-attempt, not total. |

### Library defaults

| Option | Default |
|--------|---------|
| `ResilienceEnabled` | `true` |
| `TimeoutSeconds` | 180 (per attempt; overridden by strategy `TimeoutSeconds`) |
| `MaxRetries` | 1 |
| `RetryBaseDelayMs` | 2000 |
| `RetryBackoff` | `"exponential"` |
| `BreakAfterConsecutiveFailures` | 5 |
| `BreakDurationSeconds` | 30 |
| `FallbackStrategy` | `null` (disabled) |

The strategy `TimeoutSeconds` from your config file is automatically used as the
per-attempt timeout, overriding the 180 s library default. If your strategy sets
`TimeoutSeconds: 900`, every attempt gets 900 s before timing out.

### Transient vs. non-transient errors

Retried on transient errors: `server_error`, `network`, `timeout`, `malformed_response`,
`empty_response`.

Not retried: `auth`, `rate_limit`, `AttachmentTooLarge`, `spawn_failed`. These are
surfaced immediately because retrying cannot fix them.

### Per-test resilience overrides

Override individual options on `[AiFact]` or `[AiTheory]`:

```csharp
// Give this specific test a longer timeout and more retries.
[AiFact(TimeoutSeconds = 300, MaxRetries = 3)]
public async Task LongRunningAnalysis()
{
    var response = await AiStrategyFixture.Default.ExecuteAsync(
        new FrontierRequest("Analyze deeply.", input),
        attribute.GetResilienceOptions(AiStrategyFixture.Default.StrategyResilienceOptions));
    // ...
}
```

Available per-test options on both `[AiFact]` and `[AiTheory]`:

| Property | Type | Sentinel | Description |
|----------|------|---------|-------------|
| `TimeoutSeconds` | `int` | `-1` | Per-attempt timeout in seconds. |
| `MaxRetries` | `int` | `-1` | Retry attempts on transient failure. |
| `RetryBaseDelayMs` | `int` | `-1` | Base retry delay in milliseconds. |
| `RetryBackoff` | `string?` | `null` | `"exponential"`, `"linear"`, or `"constant"`. |
| `BreakAfterConsecutiveFailures` | `int` | `-1` | Failures before circuit opens. |
| `BreakDurationSeconds` | `int` | `-1` | Seconds the circuit stays open. |
| `FallbackStrategy` | `string?` | `null` | Named strategy to fall back to. |
| `ResilienceEnabled` | `bool?` | `null` | `false` disables the pipeline entirely for this test. |

Sentinel values (`-1` / `null`) inherit from the strategy config; set a positive value
to override for a specific test.

### Disabling resilience for a test

```csharp
[AiFact(ResilienceEnabled = false)]
public async Task DirectCall_NoRetry()
{
    // Pipeline bypassed; the raw client is called directly.
}
```

---

## Review Attributes

Review attributes generate xUnit data rows that call a frontier model and pass the
result JSON to the test method. They are stackable and support single or multiple
agents.

### Available attributes

| Attribute | Built-in prompt |
|-----------|----------------|
| `[CodeReview]` | Structured code quality, correctness, and security analysis. |
| `[PlanReview]` | Implementation plan feasibility and completeness review. |
| `[ProjectReview]` | High-level project health and architecture assessment. |

### Basic usage

```csharp
using System.Text.Json;
using SharpNinja.AiUnit.Review;
using Xunit;

public class ReviewTests
{
    [Theory]
    [CodeReview]
    public void CodeReview_NoBlockingFindings(string prompt, string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;

        Assert.Equal("aiunit.review.findings.v1",
            root.GetProperty("schemaVersion").GetString());

        var blocking = root
            .GetProperty("findings")
            .EnumerateArray()
            .Where(f => f.GetProperty("severity").GetString() is "critical" or "high")
            .ToArray();

        Assert.Empty(blocking);
    }
}
```

### Custom prompt

Pass a string to override the built-in prompt:

```csharp
[CodeReview("Focus specifically on error-handling coverage and null-safety.")]
public void Review_NullSafety(string prompt, string resultJson) { ... }
```

### Selecting an agent

```csharp
// Single named strategy
[CodeReview(Agent = "claude")]

// Multiple agents - findings are aggregated
[CodeReview(Agents = new[] { "claude", "codex-subscription" })]

// Inline agent (no appsettings entry required)
[CodeReview(Kind = "cli", Command = "claude", Model = "claude-opus-4-5")]
```

### Review result JSON schema

The `resultJson` parameter always conforms to `aiunit.review.findings.v1`:

```json
{
  "schemaVersion": "aiunit.review.findings.v1",
  "reviewType": "code",
  "status": "fail",
  "summary": "One high-severity issue found.",
  "reviewedScope": "src/MyProject/Serializer.cs",
  "agent": {
    "name": "codex-subscription",
    "provider": "codex-subscription:codex",
    "model": "(cli-managed)"
  },
  "findings": [
    {
      "severity": "high",
      "category": "correctness",
      "title": "Missing null check on optional payload",
      "detail": "ReadPayload() dereferences .Value without checking JsonValueKind.Null.",
      "recommendation": "Add a null-kind guard before accessing .Value.",
      "filePath": "src/MyProject/Serializer.cs",
      "line": 42,
      "ruleId": "MY-NULL-001",
      "confidence": 0.92,
      "agent": "codex-subscription"
    }
  ]
}
```

| Field | Values | Meaning |
|-------|--------|---------|
| `status` | `pass` / `fail` / `error` | `pass` = no blocking findings; `fail` = issues found; `error` = review could not complete |
| `severity` | `critical` / `high` / `medium` / `low` / `info` | Finding severity |
| `category` | `correctness` / `security` / `performance` / `style` / `design` | Finding category |

---

## JSON Assertions

`AiUnitJsonAssertions` provides guards for validating model-generated JSON without
writing custom deserialization code.

```csharp
using System.Text.Json;
using SharpNinja.AiUnit.Validation;

[AiFact]
public async Task Model_ReturnsValidSchema()
{
    var response = await AiStrategyFixture.Default.Client!.SendAsync(
        new FrontierRequest(
            "Return JSON: {\"intent\": string, \"confidence\": number}",
            "Book a flight to Rome",
            RequireJsonOutput: true));

    Assert.Null(response.Error);

    using var doc = JsonDocument.Parse(response.Text!);
    var root = doc.RootElement;

    // Assert required keys exist
    AiUnitJsonAssertions.Required(root, "intent", "confidence");

    // Assert field values are within an allowed set
    AiUnitJsonAssertions.EnumIn(root, "intent",
        "book_flight", "cancel_reservation", "check_status");

    // Assert an array field exists and is non-empty
    AiUnitJsonAssertions.StringArray(root, "tags");
}
```

Available helpers:

| Method | Behavior |
|--------|----------|
| `Required(root, keys...)` | Assert all keys exist as properties of the root object. |
| `EnumIn(root, key, values...)` | Assert `root[key]` is a string in the allowed set. |
| `StringArray(root, key)` | Assert `root[key]` is a JSON array with at least one string element. |
| `ObjectArrayRequired(root, key, subKeys...)` | Assert `root[key]` is a non-empty array of objects each containing `subKeys`. |

Failures throw `AiResponseValidationException` with the offending field name in the
message.

---

## aiunit CLI Tool

The `aiunit` tool scans workspaces, inspects strategy configuration, validates setup,
and manages strategy catalogs from the terminal.

### Install

```
dotnet tool install --global SharpNinja.aiUnit.Tool
```

### One-shot commands

```powershell
# Scan workspace for aiUnit-enabled projects
aiunit scan --workspace F:\GitHub\MyProject

# List discovered projects with their active strategy
aiunit list --workspace F:\GitHub\MyProject

# Inspect one project
aiunit show MyProject.Tests --workspace F:\GitHub\MyProject

# Validate all project configs
aiunit validate --workspace F:\GitHub\MyProject

# Show available strategy catalog entries
aiunit catalog --workspace F:\GitHub\MyProject

# Apply a strategy to one project (dry-run first)
aiunit apply codex-subscription --project MyProject.Tests --dry-run
aiunit apply codex-subscription --project MyProject.Tests

# Apply a strategy to all discovered projects
aiunit apply-global codex-subscription --dry-run

# Restore a project to a previous snapshot
aiunit restore MyProject.Tests --snapshot backups/MyProject.Tests.2026-05-29.json

# Print version
aiunit --version

# Full help
aiunit --help
```

### Interactive REPL

```powershell
aiunit repl --workspace F:\GitHub\MyProject
```

The REPL accepts the same commands interactively with tab completion and history.
Type `help` at the `aiunit>` prompt for a command reference.

### Terminal UI

```powershell
aiunit tui overview --workspace F:\GitHub\MyProject
```

Launches a full-screen TUI with keyboard-navigable workspace overview, per-project
strategy editing, global strategy application, snapshot management, and validation
status.

---

## Advanced Usage

### Sharing `AiStrategyFixture` across a collection

```csharp
[CollectionDefinition("AI")]
public class AiCollection : ICollectionFixture<AiStrategyFixture> { }

[Collection("AI")]
public class ProductionModelTests
{
    private readonly AiStrategyFixture _fx;
    public ProductionModelTests(AiStrategyFixture fx) => _fx = fx;

    [AiFact]
    public async Task Classification_ReturnsKnownCategory()
    {
        Skip.If(!_fx.IsResolved, _fx.SkipReason);
        var resp = await _fx.Client!.SendAsync(...);
        // ...
    }
}
```

### Using `ExecuteAsync` with per-test resilience options

`AiStrategyFixture.ExecuteAsync` accepts an optional `ResilienceOptions` to build a
temporary pipeline for a single call:

```csharp
[AiFact(TimeoutSeconds = 300, MaxRetries = 3)]
public async Task SlowTest_UsesLongTimeout()
{
    var attr  = (AiFactAttribute)GetType()
        .GetMethod(nameof(SlowTest_UsesLongTimeout))!
        .GetCustomAttributes(typeof(AiFactAttribute), false)[0];

    var opts  = attr.GetResilienceOptions(
        AiStrategyFixture.Default.StrategyResilienceOptions);

    var resp  = await AiStrategyFixture.Default.ExecuteAsync(
        new FrontierRequest("sys", "user"), opts);

    Assert.Null(resp.Error);
}
```

### Building a custom frontier client

Implement `IFrontierModelClient` to integrate any provider:

```csharp
public sealed class MyCustomClient : IFrontierModelClient
{
    public string Provider => "custom";
    public string ModelVersion => "my-model-v1";

    public async Task<FrontierResponse> SendAsync(
        FrontierRequest request,
        CancellationToken cancellationToken = default)
    {
        // Call your endpoint here.
        // Always return FrontierResponse; never throw except on OCE.
        try
        {
            var text = await CallMyApiAsync(request.UserMessage, cancellationToken);
            return new FrontierResponse(text, FrontierTokenUsage.Zero,
                latencyMs: 0, "custom", "my-model-v1", null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FrontierResponse(null, FrontierTokenUsage.Zero,
                latencyMs: 0, "custom", "my-model-v1", null,
                new FrontierError("unexpected", ex.Message, null));
        }
    }
}
```

Wrap it in `ResilientFrontierClient` to get the full pipeline:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using SharpNinja.AiUnit.Resilience;

var inner  = new MyCustomClient();
var opts   = ResilienceOptions.LibraryDefault with { TimeoutSeconds = 120, MaxRetries = 2 };
var client = new ResilientFrontierClient(inner, opts, NullLogger<ResilientFrontierClient>.Instance);
```

### Scenario catalogs

`AiUnitScenarioCatalog.LoadAll<T>` locates a marker directory by walking up from the
test output directory and loads scenario files relative to it:

```csharp
var scenarios = AiUnitScenarioCatalog.LoadAll<MyScenario>(
    markerDir => Directory.GetFiles(markerDir, "scenarios/*.json")
                          .Select(File.ReadAllText)
                          .Select(json => JsonSerializer.Deserialize<MyScenario>(json)!));
```

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.

---

## Contributing

Bug reports and pull requests welcome on [GitHub](https://github.com/sharpninja/aiUnit).

---

## License

MIT. See [LICENSE](LICENSE).
