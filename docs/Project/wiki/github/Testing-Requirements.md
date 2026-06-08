# Testing Requirements (MCP Server)

## TEST-AIUNIT

### TEST-AIUNIT-001

Frontier contracts round-trip via System.Text.Json | file=tests/SharpNinja.AiUnit.Tests/Frontier/FrontierContractsTests.cs | method=Contracts_RoundTripJson


### TEST-AIUNIT-002

Frontier namespaces resolve from consumer assembly | file=tests/SharpNinja.AiUnit.Tests/Frontier/FrontierContractsTests.cs | method=Contracts_NamespacesResolve


### TEST-AIUNIT-004

ClaudeFrontierClient parses content text blocks | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_ParsesContentTextBlocks


### TEST-AIUNIT-005

ClaudeFrontierClient handles refusal | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_RefusalReturnsRefusalText


### TEST-AIUNIT-006

ClaudeFrontierClient encodes image attachments as base64 source | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_ImageAttachment_EncodedAsBase64Source


### TEST-AIUNIT-007

ClaudeFrontierClient rejects oversize attachment | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_OversizeAttachment_RejectedBeforeSend


### TEST-AIUNIT-008

ClaudeFrontierClient bearer auth header set | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_AuthHeaderIncluded


### TEST-AIUNIT-009

ClaudeFrontierClient surfaces 401 as auth_error | file=tests/SharpNinja.AiUnit.Tests/Frontier/ClaudeFrontierClientTests.cs | method=SendAsync_Http401_ReturnsAuthError


### TEST-AIUNIT-010

OpenAiCompatibleFrontierClient parses choices[0].message.content | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=SendAsync_ParsesChoicesMessageContent


### TEST-AIUNIT-011

OpenAiCompatibleFrontierClient response_format json_object flag | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=SendAsync_RequireJsonOutput_SetsResponseFormatJsonObject


### TEST-AIUNIT-012

OpenAiCompatibleFrontierClient encodes image_url as data URI | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=SendAsync_ImageAttachment_EncodedAsImageUrlDataUri


### TEST-AIUNIT-013

OpenAiCompatibleFrontierClient cost rates per family | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=EstimateCostUsd_PerFamilyRates


### TEST-AIUNIT-014

OpenAiCompatibleFrontierClient sets max_tokens and temperature | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=SendAsync_MaxTokensAndTemperatureSerialized


### TEST-AIUNIT-015

OpenAiCompatibleFrontierClient passes Bearer auth | file=tests/SharpNinja.AiUnit.Tests/Frontier/OpenAiCompatibleFrontierClientTests.cs | method=SendAsync_AuthHeaderBearerIncluded


### TEST-AIUNIT-016

GeminiFrontierClient parses candidates[0].content.parts[0].text | file=tests/SharpNinja.AiUnit.Tests/Frontier/GeminiFrontierClientTests.cs | method=SendAsync_ParsesCandidatesContentPartsText


### TEST-AIUNIT-017

GeminiFrontierClient encodes systemInstruction | file=tests/SharpNinja.AiUnit.Tests/Frontier/GeminiFrontierClientTests.cs | method=SendAsync_SystemPrompt_EncodedAsSystemInstruction


### TEST-AIUNIT-018

GeminiFrontierClient encodes inlineData for image attachments | file=tests/SharpNinja.AiUnit.Tests/Frontier/GeminiFrontierClientTests.cs | method=SendAsync_ImageAttachment_EncodedAsInlineData


### TEST-AIUNIT-019

GeminiFrontierClient uses key= query param auth | file=tests/SharpNinja.AiUnit.Tests/Frontier/GeminiFrontierClientTests.cs | method=SendAsync_AuthAsKeyQueryParam


### TEST-AIUNIT-020

FrontierClientBase retries 502/503/504 | file=tests/SharpNinja.AiUnit.Tests/Frontier/FrontierClientBaseRetryTests.cs | method=SendAsync_Http503_RetriedUpToThreeTimes


### TEST-AIUNIT-021

FrontierClientBase gives up after max retries | file=tests/SharpNinja.AiUnit.Tests/Frontier/FrontierClientBaseRetryTests.cs | method=SendAsync_RetriesExhausted_ReturnsServerError


### TEST-AIUNIT-022

FrontierClientBase normalizes 429 to rate_limit | file=tests/SharpNinja.AiUnit.Tests/Frontier/FrontierClientBaseRetryTests.cs | method=SendAsync_Http429_ReturnsRateLimit


### TEST-AIUNIT-023

CliFrontierClient Claude JSON output extracts result field | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_ClaudeJsonOutput_ExtractsResultField


### TEST-AIUNIT-024

CliFrontierClient Codex raw stdout returns verbatim | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_CodexRawStdout_ReturnsVerbatim


### TEST-AIUNIT-025

CliFrontierClient Claude non-JSON falls back to raw text | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_ClaudeNonJsonStdout_FallsBackToRawText


### TEST-AIUNIT-026

CliFrontierClient non-zero exit returns cli_error | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_NonZeroExit_ReturnsCliError_WithStderrExcerpt


### TEST-AIUNIT-027

CliFrontierClient times out gracefully | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_TimedOut_ReturnsTimeoutError


### TEST-AIUNIT-028

CliFrontierClient empty stdout returns empty_response | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_EmptyStdout_ReturnsEmptyResponseError


### TEST-AIUNIT-029

CliFrontierClient writes image attachment as temp file referenced in prompt | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_ImageAttachment_WritesTempFile_ReferencedInPrompt


### TEST-AIUNIT-030

CliFrontierClient inlines text attachment under fenced section | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_TextAttachment_InlinedUnderFencedSection


### TEST-AIUNIT-031

CliFrontierClient Claude concrete model adds --model flag | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_ClaudeConcreteModel_AddsModelFlag


### TEST-AIUNIT-032

CliFrontierClient Claude placeholder model omits --model | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_ClaudePlaceholderModel_OmitsModelFlag


### TEST-AIUNIT-033

CliFrontierClient Codex concrete model adds --model flag | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_CodexConcreteModel_AddsModelFlag


### TEST-AIUNIT-034

CliFrontierClient unknown command falls back to positional arg | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_UnknownCommand_PassesPromptAsPositionalArg


### TEST-AIUNIT-035

CliFrontierClient temp workspace cleaned up after success | file=tests/SharpNinja.AiUnit.Tests/Cli/CliFrontierClientTests.cs | method=SendAsync_TempWorkspace_CleanedUpAfterSuccess


### TEST-AIUNIT-036

AiUnitStrategyLoader finds bundled appsettings with all strategies | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=TryLoad_FindsBundledAppsettings_WithAllStrategies


### TEST-AIUNIT-037

ResolveActive defaults to configured strategy | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=ResolveActive_DefaultsToConfiguredStrategy


### TEST-AIUNIT-038

AIUNIT_STRATEGY env overrides JSON ActiveStrategy | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=ResolveActive_EnvVarOverridesJsonActive


### TEST-AIUNIT-039

ClaudeCli strategy builds without API key (Sonnet 4.6) | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_ClaudeCli_NoApiKeyNeeded_BuildsCliClient_WithSonnet46Model


### TEST-AIUNIT-040

CodexCli strategy builds without API key | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_CodexCli_NoApiKeyNeeded_BuildsCliClient


### TEST-AIUNIT-041

API-kind strategy returns skip reason when key missing | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_ApiStrategy_WithoutApiKey_ReturnsSkipReason


### TEST-AIUNIT-042

OpenAI-compatible strategy with key builds client | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_OpenAiCompatibleStrategy_WithApiKey_BuildsClient


### TEST-AIUNIT-043

Anthropic API strategy with key builds client | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_AnthropicApiStrategy_WithApiKey_BuildsClient


### TEST-AIUNIT-044

Codex API strategy with key builds OpenAI-compatible client | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_CodexApiStrategy_WithApiKey_BuildsOpenAiCompatibleClient


### TEST-AIUNIT-045

Gemini strategy with key builds Gemini client | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_GeminiStrategy_WithApiKey_BuildsGeminiClient


### TEST-AIUNIT-046

Gemini strategy without key returns skip reason | file=tests/SharpNinja.AiUnit.Tests/Strategy/AiUnitStrategyResolverTests.cs | method=Build_GeminiStrategy_WithoutApiKey_ReturnsSkipReason


### TEST-AIUNIT-047

'[AiFact] auto-skips when no strategy resolves' | file=tests/SharpNinja.AiUnit.Tests/Xunit/AiAttributeIntegrationTests.cs | method=AiFact_SkipsWhenNoStrategyResolves


### TEST-AIUNIT-048

'[AiFact] runs when strategy resolves' | file=tests/SharpNinja.AiUnit.Tests/Xunit/AiAttributeIntegrationTests.cs | method=AiFact_RunsWhenStrategyResolves


### TEST-AIUNIT-049

'[AiTheory] still honors per-row Skip.If' | file=tests/SharpNinja.AiUnit.Tests/Xunit/AiAttributeIntegrationTests.cs | method=AiTheory_PerRowSkipStillUsesSkipIf


### TEST-AIUNIT-050

AiStrategyFixture.Default lazily resolves once | file=tests/SharpNinja.AiUnit.Tests/Xunit/AiAttributeIntegrationTests.cs | method=AiStrategyFixture_LazyResolvesOnce


### TEST-AIUNIT-051

AiUnitJsonAssertions.Required throws on missing key | file=tests/SharpNinja.AiUnit.Tests/Validation/AiUnitJsonAssertionsTests.cs | method=Required_MissingKey_Throws


### TEST-AIUNIT-052

AiUnitJsonAssertions.EnumIn validates against allowed list | file=tests/SharpNinja.AiUnit.Tests/Validation/AiUnitJsonAssertionsTests.cs | method=EnumIn_ValidatesMembership


### TEST-AIUNIT-053

AiUnitJsonAssertions.StringArray rejects non-array | file=tests/SharpNinja.AiUnit.Tests/Validation/AiUnitJsonAssertionsTests.cs | method=StringArray_RejectsNonArray


### TEST-AIUNIT-054

AiUnitJsonAssertions.ObjectArrayRequired validates per-item keys | file=tests/SharpNinja.AiUnit.Tests/Validation/AiUnitJsonAssertionsTests.cs | method=ObjectArrayRequired_ValidatesPerItemKeys


### TEST-AIUNIT-055

AiUnitScenarioCatalog walks up from BaseDirectory to marker | file=tests/SharpNinja.AiUnit.Tests/Scenarios/AiUnitScenarioCatalogTests.cs | method=Walker_LocatesMarkerFolder_FromBaseDirectory


### TEST-AIUNIT-057

Subtree split produces standalone build | file=tests/SharpNinja.AiUnit.Tests/Detach/SubtreeSplitTests.cs | method=SubtreeSplit_ProducesStandaloneBuild


### TEST-AIUNIT-058

NuGet pack smoke import succeeds | file=tests/SharpNinja.AiUnit.Tests/Detach/NuGetPackSmokeTests.cs | method=NuGetPack_SmokeImport


### TEST-AIUNIT-059

AiReviewAttributeTests verifies canned prompt fallback, custom prompt preservation, stackable DataAttribute usage, and AiReviewFindingsSchema JSON shape.


### TEST-AIUNIT-060

AiReviewAttributeTests verifies a review attribute creates a two-parameter data row containing the effective prompt and result JSON, with JSON-mode request settings and the review schema attached.


### TEST-AIUNIT-061

AiReviewAttributeTests verifies multiple review agents execute independently and the default agent aggregates their results to a single JSON review.


### TEST-AIUNIT-062

AiReviewAttributeTests verifies Agent, Kind, Command, Model, TimeoutSeconds, Temperature, and MaxTokens properties flow into the review execution request.


### TEST-AIUNIT-063

AiReviewAttributeTests verifies the default prompt YAML file names, embedded YAML resources, prompt-block parsing, and empty CodeReview prompt fallback through the YAML-loaded prompt.


### TEST-AIUNIT-064

AiReviewAttributeTests verifies the loaded default code, plan, and project prompts are pre-populated with distinct scope-specific language and reviewedScope guidance.


### TEST-AIUNIT-065

AiReviewAttributeTests verifies every built-in default prompt contains the concrete AiReviewFindingsSchema.JsonSchema reply contract and does not expose an unresolved reviewFindingsJsonSchema token.


### TEST-AIUNIT-066

Local validation runs Release dotnet test, dotnet pack to a temporary output, local dotnet tool install and aiunit smoke checks, git diff --check, and a temporary stable version tag pack smoke that proves full-release package versions are produced. Azure NuGet push requires NuGetApiKey and a stable version tag.


### TEST-AIUNIT-067

AiUnitReplCommandLineTests.ExecuteAsync_Repl_ProcessesScriptedCommands verifies repl mode processes list, show, and exit against a temporary workspace, and ExecuteAsync_Repl_ReportsInvalidCommandAndContinues verifies invalid commands report errors while later commands still execute.


### TEST-AIUNIT-068

Unit tests: AiReviewJson.InjectRunLog adds runLog to a valid review object and preserves existing properties; executor injects runLog on pass-through, wrapped-error, and multi-agent aggregate paths using a fake in-memory sink; AiUnitResultsLocator resolves directory/url precedence (env > config > default) and builds sortable filenames; findings schema contains optional runLog. Integration tests: FileAiReviewRunLogSink writes a valid JSON run-log file to a temp directory with a sortable name, executor end-to-end produces review JSON whose runLog.path exists on disk and contains the run, and AiUnitStrategyLoader.TryLoad parses an appsettings file containing AiUnit.Results.


### TEST-AIUNIT-069

BuildVersioningTests verifies automatic build-number package suffixing, explicit Version overrides, stable build-number sanitization, latest-package redeploy selection, and disabled GitVersion property overwrites. ProgramCommandLineTests and redeploy smoke validation cover aiunit-review --help, --version, and --probe-exit so deployed global-tool builds can be identified and launched without Avalonia startup side effects.



## TEST-AIUNIT-RESILIENCE

### TEST-AIUNIT-RESILIENCE-001

GIVEN a ResilientFrontierClient wrapping a StubFrontierClient WHEN the stub is configured to timeout/fail/break THEN the pipeline applies the correct resilience behavior without live model calls



## TEST-AIUNITDESKTOP

### TEST-AIUNITDESKTOP-001

Cover loading 4 scenarios from a configured folder set, relative-path resolution against wireframes and screenshots folders, absolute-path passthrough, missing-file placeholder behavior, and sidecar verdict merge. Uses an in-memory file system fake for mocks-first phase before real I O.


### TEST-AIUNITDESKTOP-002

Cover load and save JSON round-trip including UndockedWindowBounds, missing-file yields default settings, malformed-file recovery. Settings file path resolves correctly per platform.


### TEST-AIUNITDESKTOP-003

Cover claude and codex resolution from a synthetic strategy config, grok built-in fallback when no matching strategy, ordering of returned AgentCommand list.


### TEST-AIUNITDESKTOP-004

Cover Launch, Kill, WriteInput, ProcessExited contract on the ITerminal abstraction with a fake terminal. Includes graceful kill on agent change (2 second SIGTERM grace) and process restart.


### TEST-AIUNITDESKTOP-005

Cover scenario ordering by numeric prefix, selection raising PropertyChanged, active-item highlight flag, and re-load after settings change.


### TEST-AIUNITDESKTOP-006

Cover PasteScenarioCommand calls ITerminal WriteInput exactly once with the active scenarios ModelPayloadYaml plus newline. CanExecute is false when no agent is running or no scenario selected.


### TEST-AIUNITDESKTOP-007

Cover dock toggle command IsDocked round-trip, UndockedWindowBounds round-trip in AppSettings, on-screen clamp when prior monitor is missing, and DataContext sharing across dock and undock.


### TEST-AIUNITDESKTOP-008

Cover sidecar write and read round-trip for approved, rejected, and needs-changes verdicts with notes. Clear deletes the file. Loader merges sidecar into scenario HumanReview.


### TEST-AIUNITDESKTOP-009

Cover regex-detect a fenced json block in a synthetic transcript, balanced-brace trailing object fallback, ignore non-JSON content, write file under artifacts aiunit-desktop-reviews with correct UTC formatting.


### TEST-AIUNITDESKTOP-010

Cover known-good JSON passes resultSchema validation, known-bad JSON yields per-rule violations, score json side-output structure.


### TEST-AIUNITDESKTOP-011

Cover ordered iteration across 4 scenarios using a fake ITerminal and fake IResultCapture, per-scenario timeout enforcement, cancellation behavior (current scenario finishes then exits), summary json roll-up structure.


### TEST-AIUNITDESKTOP-012

Cover Windows OS path returns Segoe MDL2 codepoint mapping for all logical icon names, non-Windows OS path returns Projektanker SVG identifier mapping, no missing-name entries.


### TEST-AIUNITDESKTOP-013

Cover Program Main returning 0 immediately when probe-exit flag is in args, without instantiating MainWindow. Verifies the headless CI smoke contract.


### TEST-AIUNITDESKTOP-014

Manual end-to-end validation: full macro replay across all 4 scenarios with a real agent (claude or codex or grok), verdict persistence across relaunch, undock and redock on a second monitor. Documented in test plan, not auto-executed.


### TEST-AIUNITDESKTOP-015

WireframeScenarioLoaderTests.MarkupImageViewerBaseScaleSizingTests AC_RENDER_001 through AC_RENDER_014 cover fit sizing, constructor part resolution, source timing, view-state publication, MainWindow peer mirroring, pan and zoom offset clamping, native ScrollViewer offset mirroring, editable text note markup, text-input key handling, stale vertical viewport clamping, markup undo and redo behavior, and toolbar/shortcut wiring. ProgramCommandLineTests covers aiunit-review --help, --version, and --probe-exit command-line smoke behavior.



## TEST-AIUNITREPL

### TEST-AIUNITREPL-001

AiUnitReplCommandLineTests.ExecuteAsync_Repl_ProcessesScriptedCommands verifies repl mode processes list, show, and exit against a temporary workspace, and ExecuteAsync_Repl_ReportsInvalidCommandAndContinues verifies invalid commands report errors while later commands still execute.



## TEST-LOBBY

### TEST-LOBBY-001

RiskyStars.Tests/Screens/LobbySurfaceTokenTests covers every lobby surface with token + role + reactivity facts (20 facts). Pairs with RiskyStars.Tests/Components/ThemedTableTests, RiskyStars.Tests/MenuLobbyLayoutRegressionTests, RiskyStars.Tests/LobbyScreenConsolidationTests, and RiskyStars.Tests/UiScreenConstructionTests. Build is 0 warnings / 0 errors; the Theme|Components|Screens|UiScreenConstruction|MenuLobbyLayoutRegression|LobbyScreenConsolidation|ConnectionFlow filter passes 248 tests.
