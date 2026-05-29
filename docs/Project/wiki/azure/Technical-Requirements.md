# Technical Requirements (MCP Server)

## TR-AIUNIT-ARCH-001

**No consumer-specific dependencies** — The SharpNinja.AiUnit library project and source tree must not include TruckMate project references, package references, namespaces, or source dependencies.

## TR-AIUNIT-CI-001

**Azure Pipelines tagged NuGet publish workflow** — azure-pipelines.yml packs aiUnit to artifacts/nupkg, smoke-installs SharpNinja.aiUnit.Tool from the packed output, triggers on stable version tags, verifies package versions match the tag, rejects prerelease packages for full release publish, and pushes nupkg files to nuget.org with NuGetApiKey.

## TR-AIUNIT-CLI-001

**CliFrontierClient uses IProcessRunner seam** — SendAsync delegates to an injected IProcessRunner so unit tests substitute a stub instead of spawning real binaries.

## TR-AIUNIT-CLI-002

**CLI temp workspace cleanup** — Per-call temp dir under Path.GetTempPath()/aiunit-cli-<guid> deleted in finally regardless of success or failure.

## TR-AIUNIT-CLI-003

**Per-CLI flag dispatch** — claude -> --print --output-format json --dangerously-skip-permissions [--model]; codex -> exec --skip-git-repo-check [--model]; unknown -> prompt as single positional arg.

## TR-AIUNIT-CONFIG-001

**appsettings.aiunit.json JSON shape** — Top-level key "AiUnit" with ActiveStrategy and Strategies dict; per-strategy keys Kind, BaseUrl, Model, ApiKeyEnvVar, Command, TimeoutSeconds, Temperature, Description.

## TR-AIUNIT-CONFIG-002

**Strategy env var prefix AIUNIT_** — Override precedence + naming use the AIUNIT_ prefix; decoupled from any consumer's WIREFRAME_AUDIT_ legacy.

## TR-AIUNIT-CONFIG-003

**ResolvedStrategy telemetry record** — Post-merge view exposing Name, Kind, BaseUrl, Model, TimeoutSeconds, Temperature.

## TR-AIUNIT-CONFIG-004

**Bundled sample appsettings** — samples/appsettings.aiunit.json must include current examples for Codex subscription CLI, Claude Code CLI Opus, Copilot CLI Gemini, xAI Grok through the OpenAI-compatible endpoint, and HTTP fallback strategies.

## TR-AIUNIT-CORE-001

**TargetFramework=net10.0** — csproj declares <TargetFramework>net10.0</TargetFramework>.

## TR-AIUNIT-FRONTIER-001

**Frontier contracts namespace** — All FrontierContracts records live under SharpNinja.AiUnit.Frontier with stable JSON shapes.

## TR-AIUNIT-FRONTIER-002

**FrontierClientBase retry policy** — Retries 502/503/504 up to 3 times with exponential backoff; 401/403 -> auth_error; 429 -> rate_limit; other 4xx -> server_error with body excerpt.

## TR-AIUNIT-FRONTIER-003

**Oversize attachment guard** — FrontierAttachment.MaxSizeBytes = 5 MB; clients reject attachments above with FrontierError code AttachmentTooLarge before HTTP send.

## TR-AIUNIT-FRONTIER-004

**AiUnitHttpClientFactory** — Public IHttpClientFactory impl that builds a new HttpClient per call. Default factory for consumers that have no DI container.

## TR-AIUNIT-FRONTIER-005

**OpenAiCompatibleSerializer** — Single internal serializer shared by OpenAI-compatible clients; handles image_url + response_format=json_object + max_tokens + temperature.

## TR-AIUNIT-PKG-001

**NuGet metadata complete** — PackageId=SharpNinja.aiUnit, Version, Authors, Description, RepositoryUrl, PackageLicenseExpression=MIT, PackageTags, PackageReadmeFile, IncludeSymbols=true, SymbolPackageFormat=snupkg, EmbedUntrackedSources=true.

## TR-AIUNIT-PKG-002

**Standalone solution file** — The solution must reference only the aiUnit library, REPL/tool project, and tests needed to build this repository.

## TR-AIUNIT-PKG-003

**Subtree-split rehearsal** — git subtree split --prefix=libs/SharpNinja.aiUnit -b aiunit-detach + clone + build proves no upward dependency.

## TR-AIUNIT-PKG-004

**NuGet pack smoke** — dotnet pack -c Release produces a .nupkg consumable from a one-off console project that defines [AiFact] and verifies skip / run paths.

## TR-AIUNIT-REPL-001

**Separate console project** — The aiunit executable must live in a separate console project so SharpNinja.AiUnit remains reusable as a library package.

## TR-AIUNIT-REPL-002

**Structured JSON configuration editing** — Configuration changes must use structured JSON parsers and serializers instead of string replacement.

## TR-AIUNIT-REPL-003

**Deterministic project discovery** — Project discovery must be deterministic and testable through isolated temporary directories or equivalent filesystem abstractions.

## TR-AIUNIT-REPL-004

**Shared command and TUI services** — Command parsing and TUI workflows must operate over a shared application service layer.

## TR-AIUNIT-REPL-005

**Terminal UI rendering library** — TUI screens must use a proven terminal rendering library and deterministic dimensions unless a repository constraint requires a different approach.

## TR-AIUNIT-REPL-006

**Verified backup snapshots** — Backup snapshots must be stored in a predictable aiUnit-managed location and an existing backup must not be overwritten until the new backup is verified.

## TR-AIUNIT-REPL-007

**Wireframe scenario metadata** — Wireframe comparison scenarios must include the prompt, FR/TR rows, AGENTS-README-FIRST path, actual screenshot path, wireframe path, and result schema.

## TR-AIUNIT-REPL-008

**Visual comparison result validation** — aiUnit comparison tests must auto-skip when no strategy resolves, fail invalid JSON, and write comparison result artifacts when enabled.

## TR-AIUNIT-REPL-009

**Deterministic screenshot capture** — Screenshot capture must use a deterministic terminal size and seeded fixture workspace.

## TR-AIUNIT-REPL-010

**Tool package CI flow** — CI must restore, test, pack, smoke-install the aiunit .NET tool package from the produced package source, and publish to nuget.org only from the intended stable version tag flow.

## TR-AIUNIT-RESILIENCE-ATTR-001

**Attribute resilience property shape and sentinel encoding** — Each resilience property on AiFactAttribute and AiTheoryAttribute uses a sentinel meaning inherit: int properties default to -1, string properties to null, bool? to null. GetResilienceOptions(ResilienceOptions base) merges sentinel-absent values from base and returns a fully-populated ResilienceOptions. The 8 properties are: TimeoutSeconds(int), MaxRetries(int), RetryBaseDelayMs(int), RetryBackoff(string nullable), BreakAfterConsecutiveFailures(int), BreakDurationSeconds(int), FallbackStrategy(string nullable), ResilienceEnabled(bool?).

## TR-AIUNIT-RESILIENCE-PIPELINE-001

**Polly pipeline order and composition** — The resilience pipeline must be built using Polly.Core 8.5.0 on IFrontierModelClient only. Add-order (outermost first): Fallback (conditional on FallbackStrategy), CircuitBreaker, Retry, Timeout. Timeout is per-attempt. Retry ShouldHandle excludes OperationCanceledException, BrokenCircuitException, IsolatedCircuitException, and non-transient error codes (auth, rate_limit, AttachmentTooLarge, spawn_failed). CircuitBreaker uses FailureRatio=1.0, MinimumThroughput=BreakAfterConsecutiveFailures, SamplingDuration=N*max(TimeoutSeconds,30)+60s. Fallback reads the original request via AsyncLocal and invokes the alternate strategy client.

## TR-AIUNIT-RESILIENCE-PRECEDENCE-001

**Three-layer option precedence** — ResilienceOptions.Resolve(attrOpts, strategyOpts, libraryDefault) applies precedence: attribute value beats strategy-config value beats library default. Library defaults: TimeoutSeconds=180, MaxRetries=1, RetryBaseDelayMs=2000, RetryBackoff=Exponential, BreakAfterConsecutiveFailures=5, BreakDurationSeconds=30, FallbackStrategy=null, ResilienceEnabled=true. AiStrategyFixture.StrategyResilienceOptions seeds from strategy TimeoutSeconds with all other fields at library default.

## TR-AIUNIT-REVIEW-001

**Stackable review DataAttributes** — CodeReviewAttribute, PlanReviewAttribute, and ProjectReviewAttribute inherit a common AiReviewAttribute with AttributeUsage AllowMultiple=true and yield object rows containing prompt and result JSON.

## TR-AIUNIT-REVIEW-002

**Review agent resolution and aggregation** — Review attributes support Agent, Agents, and inline strategy details. A single agent returns its own normalized JSON; multiple named agents run independently and the default configured agent aggregates the individual JSON reviews into one final result.

## TR-AIUNIT-REVIEW-003

**Review findings JSON schema** — aiUnit publishes AiReviewFindingsSchema.JsonSchema for review agents and includes it in review requests as a report_review_findings tool schema while requiring JSON output.

## TR-AIUNIT-REVIEW-004

**Review result normalization** — Review execution returns valid JSON objects from agents unchanged and wraps empty, invalid, skipped, or failed review executions in schema-shaped error JSON with agent metadata and optional agentReviews.

## TR-AIUNIT-REVIEW-005

**Built-in review prompt YAML assets** — aiUnit ships code-review.yaml, plan-review.yaml, and project-review.yaml under Review/Prompts and exposes their package-relative names through AiReviewPrompts.DefaultPromptFileName.

## TR-AIUNIT-REVIEW-006

**YAML default prompt loader** — AiReviewPrompts loads embedded default prompt YAML resources, extracts the prompt field, and uses that value when an attribute prompt is null, empty, or whitespace.

## TR-AIUNIT-REVIEW-007

**Scope-specific review prompt prepopulation** — Built-in AI review prompt YAML files must be pre-populated with scope-specific guidance for code, plan, and project reviews, including priority ordering, exclusions, citation expectations, and reviewedScope reporting guidance.

## TR-AIUNIT-REVIEW-008

**Review prompts include reply JSON schema** — Built-in AI review prompts must include the JSON schema the agent should use when replying. The YAML prompt text may use a maintained schema token, but the effective prompt loaded at runtime must contain AiReviewFindingsSchema.JsonSchema and no unresolved token.

## TR-AIUNIT-SCENARIO-001

**AiUnitScenarioCatalog<T> root marker** — Walks up from AppContext.BaseDirectory looking for a configurable marker folder name. Default marker is supplied by the consumer.

## TR-AIUNIT-XUNIT-001

**[AiFact] inherits SkippableFactAttribute** — Reuses Xunit.SkippableFact 1.4.13 extensibility; no custom IXunitTestCase machinery.

## TR-AIUNIT-XUNIT-002

**AiStrategyFixture.Default is process-wide lazy** — Single Lazy<AiStrategyFixture> built from AiUnitStrategyLoader.TryLoad() + AiUnitStrategyResolver.Build via reflection bridge; thread-safe; never throws.

## TR-AIUNIT-XUNIT-003

**AiTestCollection disables parallelization** — [CollectionDefinition(DisableParallelization=true)] enforces serial AI test execution to avoid rate-limit collisions. Assembly-level fallback in test project via [assembly: CollectionBehavior(DisableTestParallelization=true)].

## TR-LOBBY-PRIMITIVES-001

**Lobby surfaces compose from Phase 1 themed component primitives** — area=LOBBY subarea=PRIMITIVES. Every lobby surface uses AppFrame.Build for the outer page frame, Card.Standard or Card.Elevated for body cards (content added via Card.Body), ThemedButton.Build (Primary/Secondary/Destructive/Disabled/Ghost roles) for action buttons, Chip.Build for status indicators, Pill.Build (Default/Success/Warning/Danger/Active) for inline status labels, SectionHeader.Build for eyebrow captions, and ScrollableBody.Build for bounded scroll viewports. The legacy ThemedUIFactory remains in use only for Myra-specific seams (ComboBox, ValidatedTextBox) where existing UI tests scrape the widget type.

## TR-LOBBY-REACTIVITY-001

**Lobby surfaces repaint on theme runtime swap** — area=LOBBY subarea=REACTIVITY. Every lobby surface subscribes to IThemeRuntime.ThemeChanged via UiComponents.Subscribe so the outer surface background, header label colors, and every Phase 1 primitive emitted by the surface (Card, Chip, Pill, ThemedButton, ThemedTable rows) repaint when ThemeRuntime.Apply swaps the active document. Subscriptions are stored so re-builds dispose the previous handler before re-subscribing.

## TR-LOBBY-TABLE-001

**Lobby tables consume the ThemedTable primitive contract** — area=LOBBY subarea=TABLE. ThemedTable.Build emits a header band whose Tag carries a HeaderSnapshot (FgToken=palette.text.secondary, DividerToken=palette.borders.subtle), a body Grid of row Panels whose Tag carries a RowSnapshot (FillToken alternating palette.surfaces.surface and palette.surfaces.elevated; states.selected.fillToken when selected), and an accent strip per row whose Tag carries a SelectedStripSnapshot (Token=palette.accent.gold, WidthPx=2 when selected). Wraps in a ScrollableBody so always-visible scrollbar contract carries forward.

