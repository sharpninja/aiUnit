# Changelog

All notable changes to `SharpNinja.aiUnit` are documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Changed
- **BREAKING:** Review attributes now carry the `Ai` prefix for consistency with
  `[AiFact]` and `[AiTheory]`: `[CodeReview]` becomes `[AiCodeReview]`,
  `[PlanReview]` becomes `[AiPlanReview]`, and `[ProjectReview]` becomes
  `[AiProjectReview]`. The backing classes are renamed to `AiCodeReviewAttribute`,
  `AiPlanReviewAttribute`, and `AiProjectReviewAttribute`. The shared base class
  `AiReviewAttribute` is unchanged. Update test method attributes when upgrading.

---

## [0.9.0] - 2026-05-29

### Added
- **Resilience pipeline** (Polly.Core 8.5.0): every `IFrontierModelClient.SendAsync`
  call is now automatically wrapped in a per-attempt timeout, retry with exponential
  backoff, circuit breaker, and optional fallback to an alternate strategy. The
  pipeline is always on by default and is configurable per-strategy or per-test.
- `ResilienceOptions` sealed record with `LibraryDefault` and full `with`-expression
  support.
- `ResilientFrontierClient` decorator implementing `IFrontierModelClient` and
  `IDisposable`; configures the Polly pipeline from a `ResilienceOptions` instance.
- Eight per-test resilience properties on `[AiFact]` and `[AiTheory]`:
  `TimeoutSeconds`, `MaxRetries`, `RetryBaseDelayMs`, `RetryBackoff`,
  `BreakAfterConsecutiveFailures`, `BreakDurationSeconds`, `FallbackStrategy`,
  `ResilienceEnabled`. Sentinel values (`-1` / `null`) inherit from strategy config.
- `AiFactAttribute.GetResilienceOptions(base)` and
  `AiTheoryAttribute.GetResilienceOptions(base)` merge sentinel properties over a
  base `ResilienceOptions`.
- `AiStrategyFixture.StrategyResilienceOptions` property exposes the strategy-level
  resolved options (strategy `TimeoutSeconds` + library defaults for everything else).
- `AiStrategyFixture.ExecuteAsync(request, overrideOpts?, ct)` for per-test option
  overrides without requiring a custom xUnit executor.

---

## [0.5.0] - 2026-05-27

### Added
- `aiunit` REPL tool: interactive command processor for workspace scanning, project
  listing, strategy catalog management, `apply`, `apply-global`, `restore`,
  `validate`, and `export` commands.
- `aiunit tui` terminal UI with keyboard-navigable overview, per-project strategy
  editing, snapshot management, and validation status.
- NuGet publication pipeline via Azure Pipelines; `dotnet pack` and push on stable
  version tags.

### Fixed
- REPL command-loop completion: `aiunit repl` now runs as a proper interactive
  processor with history and tab completion.

---

## [0.4.0] - 2026-05-22

### Added
- `[CodeReview]`, `[PlanReview]`, and `[ProjectReview]` xUnit data attributes.
  Each attribute generates a theory data row containing the effective prompt and
  review result JSON (`aiunit.review.findings.v1`).
- Built-in YAML prompt files for all three review kinds; empty or null prompt
  arguments load the canned prompt automatically.
- Multi-agent aggregation: the `Agents` property accepts multiple strategy names
  whose findings are merged by the default active strategy.
- Inline agent override properties on review attributes: `Kind`, `BaseUrl`, `Model`,
  `ApiKeyEnvVar`, `Command`, `TimeoutSeconds`, `Temperature`, `MaxTokens`.
- `AiReviewFindingsSchema` static class exposing the JSON schema version constant
  and schema document.

---

## [0.3.0] - 2026-05-15

### Added
- Google Gemini HTTP adapter (`GeminiFrontierClient`) for
  `generativelanguage.googleapis.com`.
- OpenAI-compatible adapter (`OpenAiCompatibleFrontierClient`) covering OpenAI,
  xAI (Grok), MAF, and any provider that speaks `/v1/chat/completions`.
- CLI strategy for OpenAI Codex: spawns `codex exec --skip-git-repo-check`.
- Image and text attachment support (`FrontierAttachment`): images are encoded as
  provider-native vision blocks; text attachments are inlined as fenced code.
- `AiUnitScenarioCatalog.LoadAll<T>` and `LocateMarkerDirectory` for
  directory-tree-based scenario loading.
- `AiUnitJsonAssertions`: `Required`, `EnumIn`, `StringArray`,
  `ObjectArrayRequired` guard helpers.
- `AiResponseValidationException` with the offending field name in the message.
- `samples/appsettings.aiunit.json` with ready-to-edit entries for all supported
  providers.

---

## [0.2.0] - 2026-05-08

### Added
- Anthropic Claude HTTP adapter (`ClaudeFrontierClient`) with `x-api-key` +
  `anthropic-version` headers and content-array response parsing.
- CLI subscription strategy for Claude Code: spawns
  `claude --print --output-format json` and parses the `{"result": "..."}` envelope.
- `AiUnitStrategyLoader` reads `appsettings.aiunit.json` and applies `AIUNIT_*`
  environment-variable overrides.
- `AiUnitStrategyResolver` dispatches by `Kind` to the appropriate adapter.
- `[AiFact]` and `[AiTheory]` attributes extending `SkippableFactAttribute` and
  `SkippableTheoryAttribute`.
- `AiStrategyFixture.Default` process-wide singleton; auto-skip when no strategy
  resolves.
- Transport-level retry on 502/503/504 responses (single retry in `FrontierClientBase`).
- `FrontierRequest`, `FrontierResponse`, `FrontierError`, `FrontierTokenUsage`,
  `FrontierTool`, `FrontierAttachment` contracts.
- `IFrontierModelClient` interface with documented error-vs-throw contract.

---

## [0.1.0] - 2026-04-30

### Added
- Initial package scaffold: `net10.0` target, `SharpNinja.aiUnit` NuGet ID, MIT
  license, GitVersion-based versioning, symbol packages (`.snupkg`).
- `IFrontierModelClient` interface and `FrontierRequest`/`FrontierResponse` contracts.
- `AiStrategyFixture` skeleton with reflection-based strategy type discovery.
