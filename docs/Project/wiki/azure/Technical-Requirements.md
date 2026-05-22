# Technical Requirements (MCP Server)

## AIUNIT-TR-001

**TargetFramework=net10.0** — csproj declares <TargetFramework>net10.0</TargetFramework>. Source status implemented.

## AIUNIT-TR-002

**NuGet metadata complete** — PackageId=SharpNinja.aiUnit, Version, Authors, Description, RepositoryUrl, PackageLicenseExpression=MIT, PackageTags, PackageReadmeFile, IncludeSymbols=true, SymbolPackageFormat=snupkg, EmbedUntrackedSources=true. Source status implemented.

## AIUNIT-TR-003

**No TruckMate symbol leaks under src/** — NoTruckMateLeak_GuardTest walks libs/SharpNinja.aiUnit/src/**/*.cs and asserts zero instances of "TruckMate." substring. Build-time enforcement. Source status implemented.

## AIUNIT-TR-004

**Frontier contracts namespace** — All FrontierContracts records live under SharpNinja.AiUnit.Frontier with stable JSON shapes. Source status implemented.

## AIUNIT-TR-005

**Standalone solution file** — libs/SharpNinja.aiUnit/SharpNinja.aiUnit.sln references only its own src + tests csprojs (no TruckMate refs). Source status implemented.

## AIUNIT-TR-006

**FrontierClientBase retry policy** — Retries 502/503/504 up to 3 times with exponential backoff; 401/403 → auth_error; 429 → rate_limit; other 4xx → server_error with body excerpt. Source status implemented.

## AIUNIT-TR-007

**Oversize attachment guard** — FrontierAttachment.MaxSizeBytes = 5 MB; clients reject attachments above with FrontierError code AttachmentTooLarge before HTTP send. Source status implemented.

## AIUNIT-TR-008

**AiUnitHttpClientFactory** — Public IHttpClientFactory impl that builds a new HttpClient per call. Default factory for consumers that have no DI container. Source status implemented.

## AIUNIT-TR-009

**OpenAiCompatibleSerializer** — Single internal serializer shared by OpenAI-compatible clients; handles image_url + response_format=json_object + max_tokens + temperature. Source status implemented.

## AIUNIT-TR-010

**CliFrontierClient uses IProcessRunner seam** — SendAsync delegates to an injected IProcessRunner so unit tests substitute a stub instead of spawning real binaries. Source status implemented.

## AIUNIT-TR-011

**CLI temp workspace cleanup** — Per-call temp dir under Path.GetTempPath()/aiunit-cli-<guid> deleted in finally regardless of success or failure. Source status implemented.

## AIUNIT-TR-012

**Per-CLI flag dispatch** — claude → --print --output-format json --dangerously-skip-permissions [--model]; codex → exec --skip-git-repo-check [--model]; unknown → prompt as single positional arg. Source status implemented.

## AIUNIT-TR-013

**appsettings.aiunit.json JSON shape** — Top-level key "AiUnit" with ActiveStrategy and Strategies dict; per-strategy keys Kind, BaseUrl, Model, ApiKeyEnvVar, Command, TimeoutSeconds, Temperature, Description. Source status implemented.

## AIUNIT-TR-014

**Strategy env var prefix AIUNIT_** — Override precedence + naming use the AIUNIT_ prefix; decoupled from any consumer's WIREFRAME_AUDIT_ legacy. Source status implemented.

## AIUNIT-TR-015

**ResolvedStrategy telemetry record** — Post-merge view exposing Name, Kind, BaseUrl, Model, TimeoutSeconds, Temperature. Source status implemented.

## AIUNIT-TR-016

**Bundled sample appsettings** — samples/appsettings.aiunit.json ships with maf, cline, claude (cli, Sonnet 4.6), claude-api, grok, codex (cli), codex-api, gemini strategies as a template. Source status implemented.

## AIUNIT-TR-017

**'[AiFact] inherits SkippableFactAttribute'** — Reuses Xunit.SkippableFact 1.4.13 extensibility; no custom IXunitTestCase machinery. Source status implemented.

## AIUNIT-TR-018

**AiStrategyFixture.Default is process-wide lazy** — Single Lazy<AiStrategyFixture> built from AiUnitStrategyLoader.TryLoad() + AiUnitStrategyResolver.Build via reflection bridge; thread-safe; never throws. Source status implemented.

## AIUNIT-TR-019

**AiTestCollection disables parallelization** — [CollectionDefinition(DisableParallelization=true)] enforces serial AI test execution to avoid rate-limit collisions. Assembly-level fallback in test project via [assembly: CollectionBehavior(DisableTestParallelization=true)]. Source status implemented.

## AIUNIT-TR-020

**AiUnitScenarioCatalog<T> root marker** — Walks up from AppContext.BaseDirectory looking for a configurable marker folder name. Default marker is supplied by the consumer. Source status implemented.

## AIUNIT-TR-021

**Consumer references aiUnit via ProjectReference initially** — TruckMate.Wireframes.AgentTests.csproj adds ProjectReference to ../../libs/SharpNinja.aiUnit/src/SharpNinja.AiUnit/SharpNinja.AiUnit.csproj. Migration to PackageReference is deferred until aiUnit publishes to a feed. Source status implemented.

## AIUNIT-TR-022

**Subtree-split rehearsal** — git subtree split --prefix=libs/SharpNinja.aiUnit -b aiunit-detach + clone + build proves no upward dependency. Source status planned.

## AIUNIT-TR-023

**NuGet pack smoke** — dotnet pack -c Release produces a .nupkg consumable from a one-off console project that defines [AiFact] and verifies skip / run paths. Source status implemented.

## AIUNIT-TR-024

**requirements.yaml MCP-import shape** — YAML schema matches MCP Server POST body for /requirements/fr|tr|test + /requirements/mapping (FrTrMapping {frId, trIds[]} and FrTestMapping {frId, testIds[]}). Source status implemented.

## AIUNIT-TR-025

**Stackable review DataAttributes** — CodeReviewAttribute, PlanReviewAttribute, and ProjectReviewAttribute inherit a common AiReviewAttribute with AttributeUsage AllowMultiple=true and yield object rows containing prompt and result JSON.

## AIUNIT-TR-026

**Review agent resolution and aggregation** — Review attributes support Agent, Agents, and inline strategy details. A single agent returns its own normalized JSON; multiple named agents run independently and the default configured agent aggregates the individual JSON reviews into one final result.

## AIUNIT-TR-027

**Review findings JSON schema** — aiUnit publishes AiReviewFindingsSchema.JsonSchema for review agents and includes it in review requests as a report_review_findings tool schema while requiring JSON output.

## AIUNIT-TR-028

**Review result normalization** — Review execution returns valid JSON objects from agents unchanged and wraps empty, invalid, skipped, or failed review executions in schema-shaped error JSON with agent metadata and optional agentReviews.

## AIUNIT-TR-029

**Built-in review prompt YAML assets** — aiUnit ships code-review.yaml, plan-review.yaml, and project-review.yaml under Review/Prompts and exposes their package-relative names through AiReviewPrompts.DefaultPromptFileName.

## AIUNIT-TR-030

**YAML default prompt loader** — AiReviewPrompts loads embedded default prompt YAML resources, extracts the prompt field, and uses that value when an attribute prompt is null, empty, or whitespace.

## AIUNIT-TR-031

**Scope-specific review prompt prepopulation** — Built-in AI review prompt YAML files must be pre-populated with scope-specific guidance for code, plan, and project reviews, including priority ordering, exclusions, citation expectations, and reviewedScope reporting guidance.

## AIUNIT-TR-032

**Review prompts include reply JSON schema** — Built-in AI review prompts must include the JSON schema the agent should use when replying. The YAML prompt text may use a maintained schema token, but the effective prompt loaded at runtime must contain AiReviewFindingsSchema.JsonSchema and no unresolved token.

## AIUNIT-TR-033

**Azure Pipelines NuGet publish workflow** — azure-pipelines.yml packs aiUnit to artifacts/nupkg and pushes nupkg files to nuget.org from main builds using NUGET_API_KEY from the NuGetApiKey pipeline variable.

## TR-AIUNITREPL-001

**TR-AIUNITREPL-001** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-002

**TR-AIUNITREPL-002** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-003

**TR-AIUNITREPL-003** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-004

**TR-AIUNITREPL-004** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-005

**TR-AIUNITREPL-005** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-006

**TR-AIUNITREPL-006** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-007

**TR-AIUNITREPL-007** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-008

**TR-AIUNITREPL-008** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-009

**TR-AIUNITREPL-009** — Placeholder requirement backfilled by DB-FK-001.

## TR-AIUNITREPL-010

**TR-AIUNITREPL-010** — Placeholder requirement backfilled by DB-FK-001.
