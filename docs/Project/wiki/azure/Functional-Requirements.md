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

## FR-AIUNIT-024 Review JSON includes run-log reference

Every aiUnit review result JSON (AiCodeReview/AiPlanReview/AiProjectReview) must include a runLog object referencing the persisted run log for that review run. The reference must contain the local filesystem path to the run-log file and, when an online base URL is configured, a url to the run log. The reference is present on all output paths: valid agent JSON pass-through, wrapped-error output, and multi-agent aggregate output.

## FR-AIUNIT-025 Configurable results output directory with sortable run-log filenames

aiUnit must support an optional appsettings configuration value (AiUnit.Results.OutputDirectory) specifying the directory where review run-log result files are written. Each result file name must include a sortable UTC datetime of the start of the test. When unset, a default directory (aiunit-results under the test output base directory) is used. An optional AiUnit.Results.OnlineBaseUrl supplies the base for the online run-log URL.

## FR-AIUNIT-026 Every local build and redeploy uses a unique package version

Nuke-controlled builds and local dotnet-tool redeploys must avoid reusing package versions. When no explicit Version is supplied, the build appends a sanitized build-number suffix from CI build identifiers or a UTC timestamp to the GitVersion base version, uses that value consistently for assembly informational version, package version, pack output, smoke tests, and global tool redeploy package selection, and still allows explicit release Version overrides.

## FR-AIUNITDESKTOP-001 Scenarios load from configurable folders

Desktop app loads wireframe scenarios from three user-configurable folder paths (scenarios YAML, wireframe SVG, actual screenshot PNG) instead of marker-walking the repo. Loader resolves YAML-relative paths against the configured wireframes and screenshots folders, accepts absolute paths verbatim, and surfaces a per-scenario error placeholder when a referenced image is missing.

## FR-AIUNITDESKTOP-002 First-run folder pickers and persisted settings

On first launch the Settings flyout opens automatically and asks the user to pick three folders (scenarios YAML, wireframe SVG, actual screenshot PNG). Folder pickers use Avalonia StorageProvider OpenFolderPickerAsync. Settings persist to a JSON file under user-profile AppData. Subsequent launches skip the flyout and load directly. A reload-scenarios action re-runs the loader.

## FR-AIUNITDESKTOP-003 Agent selection from strategy config with built-in grok fallback

A toolbar ComboBox lets the user pick the agent CLI to host in the embedded terminal. Items are populated from AiUnitStrategyLoader TryLoad filtered to Kind cli (claude-code, codex). Grok is provided as a built-in fallback even when no matching strategy exists in appsettings aiunit json. Selecting an agent disposes any running subprocess gracefully (2 second SIGTERM grace) and launches the new one.

## FR-AIUNITDESKTOP-004 Embedded native terminal hosts agent CLIs

Middle column of the main window hosts a native Avalonia terminal (Iciclecreek Terminal TerminalControl) that runs the selected agent CLI under a real PTY (ConPTY on Windows, forkpty on Unix). The terminal renders VT100 ANSI sequences (colors, cursor movement, alternate buffer) and supports keyboard input. Required because claude and codex switch to non-interactive mode when stdin is not a TTY.

## FR-AIUNITDESKTOP-005 Three-column layout with top scenario nav

Main window shows a top nav bar listing scenarios in numeric-prefix order with the active item highlighted, three resizable body columns (wireframe SVG left, embedded terminal middle, actual screenshot PNG right), and a bottom status bar with screen id, agent name, process status, and PID. Clicking a scenario in the nav refreshes both image panels.

## FR-AIUNITDESKTOP-006 Paste scenario YAML to agent stdin

A toolbar button (Segoe MDL2 Send glyph) writes the current scenario ModelPayloadYaml (raw YAML plus redacted AGENTS-README content) followed by newline to the terminal stdin. This primes the agent with the scenario context for human-in-the-loop review.

## FR-AIUNITDESKTOP-007 Undockable chat terminal window

A toolbar toggle moves the embedded terminal out of the main window into a separate UndockedChatWindow. The same ChatTerminalViewModel instance is the DataContext in both locations so the PTY stream and transcript continue uninterrupted across dock and undock cycles. Closing the undocked window redocks the terminal back into the middle column.

## FR-AIUNITDESKTOP-008 Undocked window position remembered per monitor

AppSettings persists the undocked window bounds (X, Y, width, height, screen display name). On re-undock the window is restored to its prior monitor and position. If the prior monitor is no longer attached, the window centers on the primary screen and bounds are sanity-clamped to remain on-screen.

## FR-AIUNITDESKTOP-009 Per-scenario human review verdict persisted as sidecar

A VerdictPanel allows the reviewer to record approved, rejected, or needs-changes plus free-text notes per scenario. Verdicts are written to a sidecar file named screenId review yaml next to the scenario YAML. The loader merges any sidecar into the in-memory WireframeScenario. Save writes the file. Clear deletes it. Status bar shows the current verdict.

## FR-AIUNITDESKTOP-010 Agent result JSON detected and saved to artifacts

The chat view watches the transcript for a JSON object matching the scenario resultSchema (fenced json block or trailing balanced-brace object). When detected, a non-modal toast offers Save, Save and Score, or Dismiss. Save writes the JSON to artifacts aiunit-desktop-reviews under the aiUnit repo root when cwd is inside it, otherwise under user-profile AppData fallback.

## FR-AIUNITDESKTOP-011 Saved JSON scored against scenario resultSchema

Save and Score additionally validates the captured JSON against the scenario embedded resultSchema using JsonSchema Net and writes a sibling score json file summarizing pass and fail per schema rule and an overall verdict.

## FR-AIUNITDESKTOP-012 Macro replay across all scenarios with summary

A Run-all toolbar button iterates scenarios in numeric-prefix order: selects each, pastes ModelPayloadYaml, waits for the agent to emit JSON (poll 250 ms with configurable per-scenario timeout, default 5 minutes), saves and scores the result under artifacts aiunit-desktop-reviews per-run folder. A MacroRunSummaryWindow shows total scenarios scored and passed at end and offers Open Folder. Cancellable mid-run.

## FR-AIUNITDESKTOP-013 Cross-platform icon kit Segoe MDL2 on Windows SVG on others

An IconKit maps logical icon names (Send, Settings, Play, FolderOpen, NewWindow, BackToWindow, Refresh, Stop, ChevronLeft, ChevronRight) to the appropriate visual: Segoe MDL2 Assets codepoint on Windows or a Projektanker Icons Avalonia SVG identifier on Linux and macOS. Detection at startup binds the right resource template so XAML stays platform-neutral.

## FR-AIUNITDESKTOP-014 Distribution as dotnet tool aiunit-review with probe-exit smoke

The Desktop app ships as a packable dotnet tool. csproj has PackAsTool true, ToolCommandName aiunit-review, PackageId SharpNinja aiUnit Desktop Tool. Users install with dotnet tool install global and run aiunit-review. The app honors a probe-exit flag that returns 0 immediately without opening a window so CI can smoke-test the package on headless agents.

## FR-AIUNITDESKTOP-015 Synchronized comparison viewer interactions and editable markup history

aiunit-review comparison panes must keep wireframe and screenshot images visually aligned during fit, mousewheel zoom, pointer panning, native scrolling, and toolbar zoom actions. Markup tools must support highlighter, arrow, editable text notes, clear, and per-pane undo/redo history without intercepting text input.

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

