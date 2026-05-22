# Functional Requirements (MCP Server)

## AIUNIT-FR-001 Self-contained library with zero TruckMate references

aiUnit ships with no ProjectReference / NuGet ref to any TruckMate or app-specific assembly. A build-time guard test fails the suite on any leak of the substring "TruckMate." in source under `libs/SharpNinja.aiUnit/src/**`. Source status implemented.

## AIUNIT-FR-002 net10.0 target framework

csproj declares <TargetFramework>net10.0</TargetFramework> to match the contemporary consumer projects. Multi-targeting is deferred. Source status implemented.

## AIUNIT-FR-003 NuGet-packable with full metadata

dotnet pack produces SharpNinja.aiUnit.0.1.0-preview.1.nupkg + .snupkg with Authors, Description, RepositoryUrl, License (MIT), PackageTags, IncludeSymbols, EmbedUntrackedSources, SymbolPackageFormat=snupkg. Source status implemented.

## AIUNIT-FR-004 Anthropic Claude HTTP adapter

ClaudeFrontierClient sends to /v1/messages with x-api-key + anthropic-version header and parses the content[].text response shape. Source status implemented.

## AIUNIT-FR-005 OpenAI-compatible HTTP adapter

OpenAiCompatibleFrontierClient is a single adapter speaking /v1/chat/completions with Bearer auth and OpenAI/xAI JSON shape; covers OpenAI, xAI, MAF, Cline, OpenAI Codex API, and any provider speaking the same surface. Cost rates resolve from model prefix. Source status implemented.

## AIUNIT-FR-006 Google Gemini HTTP adapter

GeminiFrontierClient sends to generativelanguage.googleapis.com with the API key as a `key=` query param and parses candidates[0].content.parts[0].text. Source status implemented.

## AIUNIT-FR-007 CLI subscription strategy for Claude Code

CliFrontierClient spawns `claude --print --output-format json --dangerously-skip-permissions [--model <id>]` and parses {"result":"..."} JSON. No API key required; the CLI handles auth. Source status implemented.

## AIUNIT-FR-008 CLI subscription strategy for OpenAI Codex

CliFrontierClient spawns `codex exec --skip-git-repo-check [--model <id>] <prompt>` and returns raw stdout. No API key required; the CLI handles auth. Source status implemented.

## AIUNIT-FR-009 Generic CLI fallback for arbitrary binaries

For any other Command value the runner passes the merged prompt as a single positional arg and returns stdout raw. Source status implemented.

## AIUNIT-FR-010 Image and text attachment handling for CLI strategies

Image attachments are written to a per-call temp workspace under Path.GetTempPath() and referenced inline by absolute path; text attachments are inlined under fenced --- name --- blocks. The workspace is deleted in a finally block on success and failure. Source status implemented.

## AIUNIT-FR-011 Strategy resolver via appsettings.aiunit.json

AiUnitStrategyLoader reads a top-level `AiUnit:Strategies` dict + `ActiveStrategy` from a JSON file in AppContext.BaseDirectory (with parent-directory walk). Source status implemented.

## AIUNIT-FR-012 Env var override precedence

AIUNIT_STRATEGY beats JSON ActiveStrategy beats the "claude" hard fallback. Field overrides AIUNIT_BASE_URL / AIUNIT_MODEL / AIUNIT_API_KEY / AIUNIT_TIMEOUT_SECONDS / AIUNIT_TEMPERATURE / AIUNIT_KIND / AIUNIT_COMMAND each beat the corresponding JSON value. Source status implemented.

## AIUNIT-FR-013 Strategy Kind dispatch

AiUnitStrategyResolver dispatches Kind in {anthropic, openai-compatible, gemini, cli} to the matching adapter. Unknown Kind returns null + a clear skip reason. Source status implemented.

## AIUNIT-FR-014 '[AiFact] auto-skip when no strategy resolves'

AiFactAttribute extends Xunit.SkippableFactAttribute and sets Skip=reason in its constructor when AiStrategyFixture.Default cannot resolve a client. Source status implemented.

## AIUNIT-FR-015 '[AiTheory] auto-skip + MemberData support'

AiTheoryAttribute extends Xunit.SkippableTheoryAttribute. Same Skip ctor pattern. Per-row Skip.If via AiSkip.IfNoStrategy still available for runtime predicates. Source status implemented.

## AIUNIT-FR-016 AiStrategyFixture lifecycle

AiStrategyFixture.Default is a lazy process-wide singleton; thread-safe; exposes Client, Resolved, SkipReason, IsResolved; disposes the underlying HttpClient via IDisposable. Never throws from Default. Source status implemented.

## AIUNIT-FR-017 JSON validator helpers

AiUnitJsonAssertions provides Required, EnumIn, StringArray, ObjectArrayRequired primitives over System.Text.Json.JsonElement; throws AiResponseValidationException on failure. Source status implemented.

## AIUNIT-FR-018 Generic scenario catalog walker

AiUnitScenarioCatalog.LoadAll<T> walks up from AppContext.BaseDirectory to find a marker directory and applies a consumer-supplied loader. AiUnitScenarioCatalog.LocateMarkerDirectory exposes the walk standalone. Source status implemented.

## AIUNIT-FR-019 NuGet-packable and subtree-split clean

git subtree split --prefix=libs/SharpNinja.aiUnit produces a self-contained repo that builds and packs without TruckMate. Guard test enforces. Source status implemented.

## AIUNIT-FR-020 requirements.yaml ships in repo for MCP import

A single requirements.yaml at the repo root catalogs all FR / TR / Test ids + mappings, formatted for direct POST into MCP Server after detach. Source status implemented.

## AIUNIT-FR-021 Review attributes execute configured AI reviews

aiUnit exposes CodeReview, PlanReview, and ProjectReview attributes that can be stacked on xUnit theory methods. Each attribute accepts a prompt string, falls back to a canned prompt when null or empty, resolves configured or inline agent details, and passes the effective prompt plus review result JSON as the decorated method data row.

## AIUNIT-FR-022 Default review prompts are YAML-backed

CodeReviewAttribute, PlanReviewAttribute, and ProjectReviewAttribute must use built-in YAML prompt files for their null or empty prompt fallback instead of hardcoded inline prompt text.

## AIUNIT-FR-023 Azure pipeline publishes NuGet package

Azure Pipelines must restore, test, pack, publish package artifacts, and push the aiUnit .nupkg to nuget.org from main builds using the McpServer pipeline NUGET_API_KEY mechanism.

## FR-AIUNITREPL-001 FR-AIUNITREPL-001

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-002 FR-AIUNITREPL-002

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-003 FR-AIUNITREPL-003

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-004 FR-AIUNITREPL-004

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-005 FR-AIUNITREPL-005

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-006 FR-AIUNITREPL-006

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-007 FR-AIUNITREPL-007

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-008 FR-AIUNITREPL-008

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-009 FR-AIUNITREPL-009

Placeholder requirement backfilled by DB-FK-001.

## FR-AIUNITREPL-010 FR-AIUNITREPL-010

Placeholder requirement backfilled by DB-FK-001.
