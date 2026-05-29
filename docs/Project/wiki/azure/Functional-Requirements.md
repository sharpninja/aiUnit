# Functional Requirements (MCP Server)

## FR-AIUNIT-001 Self-contained library with no consumer-specific references

aiUnit must remain a self-contained library package with no project, package, namespace, or source dependency on TruckMate or any other consumer-specific application assembly.

## FR-AIUNIT-002 net10.0 target framework

csproj declares <TargetFramework>net10.0</TargetFramework> to match the contemporary consumer projects. Multi-targeting is deferred.

## FR-AIUNIT-003 NuGet-packable with full metadata

dotnet pack produces SharpNinja.aiUnit.0.1.0-preview.1.nupkg + .snupkg with Authors, Description, RepositoryUrl, License (MIT), PackageTags, IncludeSymbols, EmbedUntrackedSources, SymbolPackageFormat=snupkg.

## FR-AIUNIT-004 Anthropic Claude HTTP adapter

ClaudeFrontierClient sends to /v1/messages with x-api-key + anthropic-version header and parses the content[].text response shape.

## FR-AIUNIT-005 OpenAI-compatible HTTP adapter

OpenAiCompatibleFrontierClient is a single adapter speaking /v1/chat/completions with Bearer auth and OpenAI/xAI JSON shape; covers OpenAI, xAI, MAF, Cline, OpenAI Codex API, and any provider speaking the same surface. Cost rates resolve from model prefix.

## FR-AIUNIT-006 Google Gemini HTTP adapter

GeminiFrontierClient sends to generativelanguage.googleapis.com with the API key as a `key=` query param and parses candidates[0].content.parts[0].text.

## FR-AIUNIT-007 CLI subscription strategy for Claude Code

CliFrontierClient spawns `claude --print --output-format json --dangerously-skip-permissions [--model <id>]` and parses {"result":"..."} JSON. No API key required; the CLI handles auth.

## FR-AIUNIT-008 CLI subscription strategy for OpenAI Codex

CliFrontierClient spawns `codex exec --skip-git-repo-check [--model <id>] <prompt>` and returns raw stdout. No API key required; the CLI handles auth.

## FR-AIUNIT-009 Generic CLI fallback for arbitrary binaries

For any other Command value the runner passes the merged prompt as a single positional arg and returns stdout raw.

## FR-AIUNIT-010 Image and text attachment handling for CLI strategies

Image attachments are written to a per-call temp workspace under Path.GetTempPath() and referenced inline by absolute path; text attachments are inlined under fenced --- name --- blocks. The workspace is deleted in a finally block on success and failure.

## FR-AIUNIT-011 Strategy resolver via appsettings.aiunit.json

AiUnitStrategyLoader reads a top-level `AiUnit:Strategies` dict + `ActiveStrategy` from a JSON file in AppContext.BaseDirectory (with parent-directory walk).

## FR-AIUNIT-012 Env var override precedence

AIUNIT_STRATEGY beats JSON ActiveStrategy beats the "claude" hard fallback. Field overrides AIUNIT_BASE_URL / AIUNIT_MODEL / AIUNIT_API_KEY / AIUNIT_TIMEOUT_SECONDS / AIUNIT_TEMPERATURE / AIUNIT_KIND / AIUNIT_COMMAND each beat the corresponding JSON value.

## FR-AIUNIT-013 Strategy Kind dispatch

AiUnitStrategyResolver dispatches Kind in {anthropic, openai-compatible, gemini, cli} to the matching adapter. Unknown Kind returns null + a clear skip reason.

## FR-AIUNIT-014 [AiFact] auto-skip when no strategy resolves

AiFactAttribute extends Xunit.SkippableFactAttribute and sets Skip=reason in its constructor when AiStrategyFixture.Default cannot resolve a client.

## FR-AIUNIT-015 [AiTheory] auto-skip + MemberData support

AiTheoryAttribute extends Xunit.SkippableTheoryAttribute. Same Skip ctor pattern. Per-row Skip.If via AiSkip.IfNoStrategy still available for runtime predicates.

## FR-AIUNIT-016 AiStrategyFixture lifecycle

AiStrategyFixture.Default is a lazy process-wide singleton; thread-safe; exposes Client, Resolved, SkipReason, IsResolved; disposes the underlying HttpClient via IDisposable. Never throws from Default.

## FR-AIUNIT-017 JSON validator helpers

AiUnitJsonAssertions provides Required, EnumIn, StringArray, ObjectArrayRequired primitives over System.Text.Json.JsonElement; throws AiResponseValidationException on failure.

## FR-AIUNIT-018 Generic scenario catalog walker

AiUnitScenarioCatalog.LoadAll<T> walks up from AppContext.BaseDirectory to find a marker directory and applies a consumer-supplied loader. AiUnitScenarioCatalog.LocateMarkerDirectory exposes the walk standalone.

## FR-AIUNIT-019 NuGet-packable standalone repository

aiUnit must build and pack as a standalone repository without upward project references or consumer-application dependencies.

## FR-AIUNIT-021 Review attributes execute configured AI reviews

aiUnit exposes AiCodeReview, AiPlanReview, and AiProjectReview attributes that can be stacked on xUnit theory methods. Each attribute accepts a prompt string, falls back to a canned prompt when null or empty, resolves configured or inline agent details, and passes the effective prompt plus review result JSON as the decorated method data row.

## FR-AIUNIT-022 Default review prompts are YAML-backed

AiCodeReviewAttribute, AiPlanReviewAttribute, and AiProjectReviewAttribute must use built-in YAML prompt files for their null or empty prompt fallback instead of hardcoded inline prompt text.

## FR-AIUNIT-023 Azure pipeline publishes NuGet packages from version tags

Azure Pipelines must restore, test, pack, publish package artifacts, smoke-test the packed aiunit tool, and push full-release aiUnit packages to nuget.org only from stable version tag builds using the NuGetApiKey pipeline variable.

## FR-AIUNITREPL-001 Discover aiUnit-enabled projects

The aiunit tool must recursively discover aiUnit-enabled projects from the current directory or a supplied root.

## FR-AIUNITREPL-002 Show project strategy status

The aiunit tool must show each discovered project with its active strategy, strategy count, config file path, package/project-reference status, and validation state.

## FR-AIUNITREPL-003 Support REPL and one-shot commands

The aiunit tool must support REPL and one-shot commands for scan, list, show, set-active, add-strategy, edit-strategy, remove-strategy, apply-global, restore, validate, and export.

## FR-AIUNITREPL-004 Provide terminal UI workflows

The aiunit tool must provide terminal UI screens for workspace overview, project strategy editing, global strategy application, strategy catalog management, and validation/deploy status.

## FR-AIUNITREPL-005 Preserve project settings during global operations

The aiunit tool must allow zero or more strategies per project while preserving unrelated project settings during selected-project and global strategy operations.

## FR-AIUNITREPL-006 Catalog reusable strategies

The aiunit tool must catalog strategies from discovered projects and make those strategies reusable in selected projects or all discovered projects.

## FR-AIUNITREPL-007 Snapshot and restore strategy configuration

The aiunit tool must create restorable snapshots before configuration mutations and allow restoring individual project strategy settings.

## FR-AIUNITREPL-008 Package as a .NET tool

The aiunit executable must be packaged and published as the SharpNinja.aiUnit.Tool NuGet .NET tool.

## FR-AIUNITREPL-009 Maintain TUI visual contracts

The repository must provide SVG wireframes for each aiunit TUI screen and capture finished TUI screenshots for each wireframe.

## FR-AIUNITREPL-010 Compare TUI screenshots with aiUnit

The test suite must compare finished TUI screenshots to their SVG wireframes with aiUnit using RiskyStars-style YAML scenarios and strict JSON result validation.

## FR-AIUNIT-RESILIENCE-001 AI-backed test calls wrapped in configurable resilience pipeline

AI-backed test calls (all IFrontierModelClient.SendAsync invocations) are
wrapped in a configurable resilience pipeline comprising per-attempt timeout,
retry with backoff and jitter, circuit breaker, and optional fallback to an
alternate strategy. The pipeline is enabled by default with library-defined
defaults and is configurable per-strategy via config and per-test via
AiFact/AiTheory attribute options.

## FR-AIUNIT-RESILIENCE-002 Resilience options exposed on AiFact and AiTheory attributes

AiFactAttribute and AiTheoryAttribute expose TimeoutSeconds, MaxRetries, RetryBaseDelayMs, RetryBackoff, BreakAfterConsecutiveFailures, BreakDurationSeconds, FallbackStrategy, and ResilienceEnabled properties with sentinel defaults (-1 / null / null). Attribute values override strategy-config values which override library defaults.

## FR-LOBBY-MIGRATION-001 Lobby surfaces consume Phase 1 themed component primitives and ThemedTable

Lobby surfaces consume Phase 1 themed component primitives (AppFrame, Card, ThemedButton, Chip, Pill, LabelValueRow, SectionHeader, ScrollableBody) and the ThemedTable primitive, resolving every color through Tokens.* dotted-path tokens. Every multi-column Grid built by a lobby surface declares RowsProportions and sets GridRow/GridColumn on every child widget so the MyraScreens_RejectImplicitRowsForMultiColumnLayouts guard passes. Theme swap via ThemeRuntime.Apply repaints lobby cards.

