$ErrorActionPreference = 'Continue'
Import-Module powershell-yaml -ErrorAction Stop

# Ensure McpRepl module is discoverable in child pwsh processes (profile-added; -NoProfile drops it)
$mcpReplDir = 'F:\GitHub\McpServer\tools\powershell'
if ($env:PSModulePath -notlike "*$mcpReplDir*") {
    $env:PSModulePath = "$mcpReplDir;$env:PSModulePath"
}

$plugin = "F:\GitHub\mcpserver-claude-code-plugin\lib\Invoke-ClaudeMcpPlugin.ps1"

function Invoke-Mcp([string]$method, [hashtable]$payload) {
    $yaml = ConvertTo-Yaml $payload
    $raw = pwsh -NoProfile -NonInteractive -NoLogo -File $plugin -Command Invoke -Method $method -Params $yaml 2>&1
    $text = ($raw | Out-String)
    $idForLog = if ($payload.ContainsKey('id')) { $payload.id } elseif ($payload.ContainsKey('frId')) { "$($payload.frId) -> $($payload.trId)$($payload.testId)" } else { '?' }
    if ($text -match 'success:\s*true') {
        Write-Host "  [ok] $method $idForLog"
        return $true
    }
    if ($text -match 'already exists|duplicate|conflict|409') {
        Write-Host "  [skip] $method $idForLog (already exists)"
        return $true
    }
    Write-Host "  [FAIL] $method $idForLog"
    Write-Host "  --output tail--"
    $raw | Select-Object -Last 12 | ForEach-Object { Write-Host "  $_" }
    return $false
}

# ---------------- FRs ----------------
$frs = @(
    @{ id='FR-AIUNITDESKTOP-001'; title='Scenarios load from configurable folders';
       description='Desktop app loads wireframe scenarios from three user-configurable folder paths (scenarios YAML, wireframe SVG, actual screenshot PNG) instead of marker-walking the repo. Loader resolves YAML-relative paths against the configured wireframes and screenshots folders, accepts absolute paths verbatim, and surfaces a per-scenario error placeholder when a referenced image is missing.' },
    @{ id='FR-AIUNITDESKTOP-002'; title='First-run folder pickers and persisted settings';
       description='On first launch the Settings flyout opens automatically and asks the user to pick three folders (scenarios YAML, wireframe SVG, actual screenshot PNG). Folder pickers use Avalonia StorageProvider OpenFolderPickerAsync. Settings persist to a JSON file under user-profile AppData. Subsequent launches skip the flyout and load directly. A reload-scenarios action re-runs the loader.' },
    @{ id='FR-AIUNITDESKTOP-003'; title='Agent selection from strategy config with built-in grok fallback';
       description='A toolbar ComboBox lets the user pick the agent CLI to host in the embedded terminal. Items are populated from AiUnitStrategyLoader TryLoad filtered to Kind cli (claude-code, codex). Grok is provided as a built-in fallback even when no matching strategy exists in appsettings aiunit json. Selecting an agent disposes any running subprocess gracefully (2 second SIGTERM grace) and launches the new one.' },
    @{ id='FR-AIUNITDESKTOP-004'; title='Embedded native terminal hosts agent CLIs';
       description='Middle column of the main window hosts a native Avalonia terminal (Iciclecreek Terminal TerminalControl) that runs the selected agent CLI under a real PTY (ConPTY on Windows, forkpty on Unix). The terminal renders VT100 ANSI sequences (colors, cursor movement, alternate buffer) and supports keyboard input. Required because claude and codex switch to non-interactive mode when stdin is not a TTY.' },
    @{ id='FR-AIUNITDESKTOP-005'; title='Three-column layout with top scenario nav';
       description='Main window shows a top nav bar listing scenarios in numeric-prefix order with the active item highlighted, three resizable body columns (wireframe SVG left, embedded terminal middle, actual screenshot PNG right), and a bottom status bar with screen id, agent name, process status, and PID. Clicking a scenario in the nav refreshes both image panels.' },
    @{ id='FR-AIUNITDESKTOP-006'; title='Paste scenario YAML to agent stdin';
       description='A toolbar button (Segoe MDL2 Send glyph) writes the current scenario ModelPayloadYaml (raw YAML plus redacted AGENTS-README content) followed by newline to the terminal stdin. This primes the agent with the scenario context for human-in-the-loop review.' },
    @{ id='FR-AIUNITDESKTOP-007'; title='Undockable chat terminal window';
       description='A toolbar toggle moves the embedded terminal out of the main window into a separate UndockedChatWindow. The same ChatTerminalViewModel instance is the DataContext in both locations so the PTY stream and transcript continue uninterrupted across dock and undock cycles. Closing the undocked window redocks the terminal back into the middle column.' },
    @{ id='FR-AIUNITDESKTOP-008'; title='Undocked window position remembered per monitor';
       description='AppSettings persists the undocked window bounds (X, Y, width, height, screen display name). On re-undock the window is restored to its prior monitor and position. If the prior monitor is no longer attached, the window centers on the primary screen and bounds are sanity-clamped to remain on-screen.' },
    @{ id='FR-AIUNITDESKTOP-009'; title='Per-scenario human review verdict persisted as sidecar';
       description='A VerdictPanel allows the reviewer to record approved, rejected, or needs-changes plus free-text notes per scenario. Verdicts are written to a sidecar file named screenId review yaml next to the scenario YAML. The loader merges any sidecar into the in-memory WireframeScenario. Save writes the file. Clear deletes it. Status bar shows the current verdict.' },
    @{ id='FR-AIUNITDESKTOP-010'; title='Agent result JSON detected and saved to artifacts';
       description='The chat view watches the transcript for a JSON object matching the scenario resultSchema (fenced json block or trailing balanced-brace object). When detected, a non-modal toast offers Save, Save and Score, or Dismiss. Save writes the JSON to artifacts aiunit-desktop-reviews under the aiUnit repo root when cwd is inside it, otherwise under user-profile AppData fallback.' },
    @{ id='FR-AIUNITDESKTOP-011'; title='Saved JSON scored against scenario resultSchema';
       description='Save and Score additionally validates the captured JSON against the scenario embedded resultSchema using JsonSchema Net and writes a sibling score json file summarizing pass and fail per schema rule and an overall verdict.' },
    @{ id='FR-AIUNITDESKTOP-012'; title='Macro replay across all scenarios with summary';
       description='A Run-all toolbar button iterates scenarios in numeric-prefix order: selects each, pastes ModelPayloadYaml, waits for the agent to emit JSON (poll 250 ms with configurable per-scenario timeout, default 5 minutes), saves and scores the result under artifacts aiunit-desktop-reviews per-run folder. A MacroRunSummaryWindow shows total scenarios scored and passed at end and offers Open Folder. Cancellable mid-run.' },
    @{ id='FR-AIUNITDESKTOP-013'; title='Cross-platform icon kit Segoe MDL2 on Windows SVG on others';
       description='An IconKit maps logical icon names (Send, Settings, Play, FolderOpen, NewWindow, BackToWindow, Refresh, Stop, ChevronLeft, ChevronRight) to the appropriate visual: Segoe MDL2 Assets codepoint on Windows or a Projektanker Icons Avalonia SVG identifier on Linux and macOS. Detection at startup binds the right resource template so XAML stays platform-neutral.' },
    @{ id='FR-AIUNITDESKTOP-014'; title='Distribution as dotnet tool aiunit-review with probe-exit smoke';
       description='The Desktop app ships as a packable dotnet tool. csproj has PackAsTool true, ToolCommandName aiunit-review, PackageId SharpNinja aiUnit Desktop Tool. Users install with dotnet tool install global and run aiunit-review. The app honors a probe-exit flag that returns 0 immediately without opening a window so CI can smoke-test the package on headless agents.' }
)

# ---------------- TRs ----------------
$trs = @(
    @{ id='TR-AIUNITDESKTOP-LOADER-001'; title='WireframeScenario record and folder-driven loader'; subarea='LOADER';
       description='Public records SharpNinja AiUnit Scenarios WireframeScenario and WireframeRequirement added to the core library. Desktop project provides WireframeScenarioLoader LoadAsync taking three folder paths returning a list of WireframeScenario. Relative paths in YAML resolved against the configured folders. Absolute paths used verbatim. Missing image files yield an error placeholder per scenario not an exception.' },
    @{ id='TR-AIUNITDESKTOP-SETTINGS-001'; title='AppSettings JSON persistence and SettingsFlyout pickers'; subarea='SETTINGS';
       description='AppSettings is a record holding ScenariosFolder, WireframesFolder, ScreenshotsFolder, LastAgent, UndockedWindowBounds. SettingsService loads and saves a JSON file at user-profile AppData with platform-neutral fallback. SettingsFlyout XAML has three folder-picker rows backed by TopLevel GetTopLevel StorageProvider OpenFolderPickerAsync.' },
    @{ id='TR-AIUNITDESKTOP-AGENT-001'; title='AgentCommandResolver reuses AiUnitStrategyLoader'; subarea='AGENT';
       description='AgentCommandResolver calls AiUnitStrategyLoader TryLoad on startup, filters config Strategies to Kind cli, and returns a list of AgentCommand records (Name, Command, Args, WorkingDir, Env). Grok is appended as a built-in entry when no matching strategy exists.' },
    @{ id='TR-AIUNITDESKTOP-TERMINAL-001'; title='ITerminal abstraction over Iciclecreek terminal control'; subarea='TERMINAL';
       description='ITerminal interface exposes Launch (workdir, exe, args), Kill, WriteInput (string), event ProcessExited (exitCode), Pid, IsRunning. Real adapter wraps Iciclecreek Terminal TerminalControl (LaunchProcess, Kill, Pid, ProcessExited). Paste-input API confirmed by pre-phase-4 spike against the loaded assembly with fallbacks documented in plan.' },
    @{ id='TR-AIUNITDESKTOP-UI-001'; title='MainWindow 3-column grid and scenario nav'; subarea='UI';
       description='MainWindow axaml uses a Grid with RowDefinitions Auto-star-Auto and ColumnDefinitions star-Auto-2star-Auto-star with GridSplitters. Top nav ItemsControl binds ScenarioListViewModel Scenarios sorted by NumericPrefix with active scenario tagged Classes active. Wireframe panel renders SVG via Avalonia Svg Skia Svg. Screenshot panel uses Image with Stretch Uniform.' },
    @{ id='TR-AIUNITDESKTOP-AGENT-002'; title='Paste-YAML command in ChatTerminalViewModel'; subarea='AGENT';
       description='ChatTerminalViewModel PasteScenarioCommand reads the active scenarios ModelPayloadYaml from the bound ScenarioViewModel and calls ITerminal WriteInput with the model payload plus newline. Disabled when no agent is running or no scenario selected.' },
    @{ id='TR-AIUNITDESKTOP-UNDOCK-001'; title='UndockedChatWindow shares VM with main window'; subarea='UNDOCK';
       description='ChatTerminalViewModel IsDocked observable property controls the swap. MainWindow column 2 width is bound to IsDocked via BoolToGridLengthConverter (0 when undocked, 2 star when docked) and splitters hidden. UndockedChatWindow DataContext is the same ChatTerminalViewModel instance so the live PTY stream is uninterrupted. Closing event sets IsDocked true.' },
    @{ id='TR-AIUNITDESKTOP-SETTINGS-002'; title='UndockedWindowBounds persistence with multi-monitor restore'; subarea='SETTINGS';
       description='AppSettings UndockedWindowBounds with X, Y, Width, Height, ScreenName captured on UndockedChatWindow Closing via Screens ScreenFromVisual. On re-undock restored when screen still present in MainWindow Screens All. Otherwise centered on primary screen. Bounds sanity-clamped to keep window on-screen.' },
    @{ id='TR-AIUNITDESKTOP-VERDICT-001'; title='VerdictStore sidecar YAML and loader merge'; subarea='VERDICT';
       description='VerdictStore Read (scenariosFolder, screenId), Write (scenariosFolder, screenId, verdict), Clear (scenariosFolder, screenId). Verdict shape includes verdict (approved or rejected or needs-changes), reviewer, reviewedAt UTC, notes. WireframeScenarioLoader merges any sibling screenId review yaml into scenario HumanReview at load time.' },
    @{ id='TR-AIUNITDESKTOP-CAPTURE-001'; title='ResultCaptureService detects and saves transcript JSON'; subarea='CAPTURE';
       description='ResultCaptureService TryDetect (transcript slice) returns found and json using regex on fenced json blocks plus a balanced-brace trailing-object fallback. SaveAsync (json, artifactsRoot, screenId, utc) writes screenId json under artifacts aiunit-desktop-reviews. Artifacts root resolved to aiUnit repo root when cwd is inside it else AppData fallback.' },
    @{ id='TR-AIUNITDESKTOP-SCORE-001'; title='ResultScoringService validates JSON against resultSchema'; subarea='SCORE';
       description='ResultScoringService ScoreAsync (capturedJson, resultSchemaJson) returns ScoreResult with passed flag and list of SchemaViolation. Uses JsonSchema Net for validation. Writes sibling score json with per-rule pass and fail summary and overall verdict.' },
    @{ id='TR-AIUNITDESKTOP-MACRO-001'; title='MacroRunner orchestrates run-all with cancellation'; subarea='MACRO';
       description='MacroRunner RunAsync (scenarios, ITerminal, IResultCapture, IResultScoring, CancellationToken) iterates scenarios in order. Paste ModelPayloadYaml, poll 250 ms for captured JSON up to per-scenario timeout (default 5 minutes, settings-configurable), save and score, advance. Emits per-scenario progress events. On cancel lets current scenario finish then exits. Writes summary json roll-up.' },
    @{ id='TR-AIUNITDESKTOP-ICON-001'; title='IconKit Windows vs cross-platform mapping'; subarea='ICON';
       description='IconKit static maps logical name to a pair of segoeCodepoint and projektankerId. At startup OperatingSystem IsWindows binds the global IconStyle resource: a TextBlock template using FontFamily Segoe MDL2 Assets on Windows or an Icon template using Projektanker Icons Avalonia on others. XAML buttons reference Static Resource icon name.' },
    @{ id='TR-AIUNITDESKTOP-PACKAGE-001'; title='Dotnet tool packaging with probe-exit smoke'; subarea='PACKAGE';
       description='csproj sets OutputType Exe, PackAsTool true, ToolCommandName aiunit-review, PackageId SharpNinja aiUnit Desktop Tool, GenerateDocumentationFile true, Title, Description, PackageTags. Program Main returns 0 immediately when args contain probe-exit without instantiating MainWindow. Azure pipeline Smoke test step extended to install the tool and run probe-exit.' }
)

# ---------------- TESTs ----------------
$tests = @(
    @{ id='TEST-AIUNITDESKTOP-001'; title='WireframeScenarioLoaderTests';
       description='Cover loading 4 scenarios from a configured folder set, relative-path resolution against wireframes and screenshots folders, absolute-path passthrough, missing-file placeholder behavior, and sidecar verdict merge. Uses an in-memory file system fake for mocks-first phase before real I O.' },
    @{ id='TEST-AIUNITDESKTOP-002'; title='AppSettingsTests';
       description='Cover load and save JSON round-trip including UndockedWindowBounds, missing-file yields default settings, malformed-file recovery. Settings file path resolves correctly per platform.' },
    @{ id='TEST-AIUNITDESKTOP-003'; title='AgentCommandResolverTests';
       description='Cover claude and codex resolution from a synthetic strategy config, grok built-in fallback when no matching strategy, ordering of returned AgentCommand list.' },
    @{ id='TEST-AIUNITDESKTOP-004'; title='AgentProcessHostTests';
       description='Cover Launch, Kill, WriteInput, ProcessExited contract on the ITerminal abstraction with a fake terminal. Includes graceful kill on agent change (2 second SIGTERM grace) and process restart.' },
    @{ id='TEST-AIUNITDESKTOP-005'; title='ScenarioListViewModelTests';
       description='Cover scenario ordering by numeric prefix, selection raising PropertyChanged, active-item highlight flag, and re-load after settings change.' },
    @{ id='TEST-AIUNITDESKTOP-006'; title='ChatTerminalViewModelTests';
       description='Cover PasteScenarioCommand calls ITerminal WriteInput exactly once with the active scenarios ModelPayloadYaml plus newline. CanExecute is false when no agent is running or no scenario selected.' },
    @{ id='TEST-AIUNITDESKTOP-007'; title='UndockViewModelAndBoundsTests';
       description='Cover dock toggle command IsDocked round-trip, UndockedWindowBounds round-trip in AppSettings, on-screen clamp when prior monitor is missing, and DataContext sharing across dock and undock.' },
    @{ id='TEST-AIUNITDESKTOP-008'; title='VerdictStoreTests';
       description='Cover sidecar write and read round-trip for approved, rejected, and needs-changes verdicts with notes. Clear deletes the file. Loader merges sidecar into scenario HumanReview.' },
    @{ id='TEST-AIUNITDESKTOP-009'; title='ResultCaptureServiceTests';
       description='Cover regex-detect a fenced json block in a synthetic transcript, balanced-brace trailing object fallback, ignore non-JSON content, write file under artifacts aiunit-desktop-reviews with correct UTC formatting.' },
    @{ id='TEST-AIUNITDESKTOP-010'; title='ResultScoringServiceTests';
       description='Cover known-good JSON passes resultSchema validation, known-bad JSON yields per-rule violations, score json side-output structure.' },
    @{ id='TEST-AIUNITDESKTOP-011'; title='MacroRunnerTests';
       description='Cover ordered iteration across 4 scenarios using a fake ITerminal and fake IResultCapture, per-scenario timeout enforcement, cancellation behavior (current scenario finishes then exits), summary json roll-up structure.' },
    @{ id='TEST-AIUNITDESKTOP-012'; title='IconKitTests';
       description='Cover Windows OS path returns Segoe MDL2 codepoint mapping for all logical icon names, non-Windows OS path returns Projektanker SVG identifier mapping, no missing-name entries.' },
    @{ id='TEST-AIUNITDESKTOP-013'; title='ProbeExitTests';
       description='Cover Program Main returning 0 immediately when probe-exit flag is in args, without instantiating MainWindow. Verifies the headless CI smoke contract.' },
    @{ id='TEST-AIUNITDESKTOP-014'; title='IntegrationManualValidation';
       description='Manual end-to-end validation: full macro replay across all 4 scenarios with a real agent (claude or codex or grok), verdict persistence across relaunch, undock and redock on a second monitor. Documented in test plan, not auto-executed.' }
)

# ---------------- FR -> TR mappings ----------------
$frToTr = @(
    @('FR-AIUNITDESKTOP-001','TR-AIUNITDESKTOP-LOADER-001'),
    @('FR-AIUNITDESKTOP-002','TR-AIUNITDESKTOP-SETTINGS-001'),
    @('FR-AIUNITDESKTOP-003','TR-AIUNITDESKTOP-AGENT-001'),
    @('FR-AIUNITDESKTOP-004','TR-AIUNITDESKTOP-TERMINAL-001'),
    @('FR-AIUNITDESKTOP-005','TR-AIUNITDESKTOP-UI-001'),
    @('FR-AIUNITDESKTOP-006','TR-AIUNITDESKTOP-AGENT-002'),
    @('FR-AIUNITDESKTOP-007','TR-AIUNITDESKTOP-UNDOCK-001'),
    @('FR-AIUNITDESKTOP-008','TR-AIUNITDESKTOP-SETTINGS-002'),
    @('FR-AIUNITDESKTOP-009','TR-AIUNITDESKTOP-VERDICT-001'),
    @('FR-AIUNITDESKTOP-010','TR-AIUNITDESKTOP-CAPTURE-001'),
    @('FR-AIUNITDESKTOP-011','TR-AIUNITDESKTOP-SCORE-001'),
    @('FR-AIUNITDESKTOP-012','TR-AIUNITDESKTOP-MACRO-001'),
    @('FR-AIUNITDESKTOP-013','TR-AIUNITDESKTOP-ICON-001'),
    @('FR-AIUNITDESKTOP-014','TR-AIUNITDESKTOP-PACKAGE-001')
)

# ---------------- FR -> TEST mappings ----------------
$frToTest = @(
    @('FR-AIUNITDESKTOP-001','TEST-AIUNITDESKTOP-001'),
    @('FR-AIUNITDESKTOP-002','TEST-AIUNITDESKTOP-002'),
    @('FR-AIUNITDESKTOP-003','TEST-AIUNITDESKTOP-003'),
    @('FR-AIUNITDESKTOP-004','TEST-AIUNITDESKTOP-004'),
    @('FR-AIUNITDESKTOP-005','TEST-AIUNITDESKTOP-005'),
    @('FR-AIUNITDESKTOP-006','TEST-AIUNITDESKTOP-006'),
    @('FR-AIUNITDESKTOP-007','TEST-AIUNITDESKTOP-007'),
    @('FR-AIUNITDESKTOP-008','TEST-AIUNITDESKTOP-007'),
    @('FR-AIUNITDESKTOP-009','TEST-AIUNITDESKTOP-008'),
    @('FR-AIUNITDESKTOP-010','TEST-AIUNITDESKTOP-009'),
    @('FR-AIUNITDESKTOP-011','TEST-AIUNITDESKTOP-010'),
    @('FR-AIUNITDESKTOP-012','TEST-AIUNITDESKTOP-011'),
    @('FR-AIUNITDESKTOP-013','TEST-AIUNITDESKTOP-012'),
    @('FR-AIUNITDESKTOP-014','TEST-AIUNITDESKTOP-013'),
    @('FR-AIUNITDESKTOP-001','TEST-AIUNITDESKTOP-014'),
    @('FR-AIUNITDESKTOP-007','TEST-AIUNITDESKTOP-014'),
    @('FR-AIUNITDESKTOP-012','TEST-AIUNITDESKTOP-014')
)

# ---------------- Execute ----------------
$ok=0; $fail=0
Write-Host ""
Write-Host "=== FRs ==="
foreach ($fr in $frs) {
    $payload = @{ id=$fr.id; title=$fr.title; description=$fr.description; area='AIUNITDESKTOP'; priority='medium' }
    if (Invoke-Mcp 'workflow.requirements.createFr' $payload) { $ok++ } else { $fail++ }
}
Write-Host ""
Write-Host "=== TRs ==="
foreach ($tr in $trs) {
    $payload = @{ id=$tr.id; title=$tr.title; description=$tr.description; area='AIUNITDESKTOP'; subarea=$tr.subarea; priority='medium' }
    if (Invoke-Mcp 'workflow.requirements.createTr' $payload) { $ok++ } else { $fail++ }
}
Write-Host ""
Write-Host "=== TESTs ==="
foreach ($t in $tests) {
    $payload = @{ id=$t.id; title=$t.title; description=$t.description; area='AIUNITDESKTOP'; priority='medium' }
    if (Invoke-Mcp 'workflow.requirements.createTest' $payload) { $ok++ } else { $fail++ }
}
Write-Host ""
Write-Host "=== FR -> TR mappings ==="
foreach ($pair in $frToTr) {
    $payload = @{ frId=$pair[0]; trId=$pair[1] }
    if (Invoke-Mcp 'workflow.requirements.createMapping' $payload) { $ok++ } else { $fail++ }
}
Write-Host ""
Write-Host "=== FR -> TEST mappings ==="
foreach ($pair in $frToTest) {
    $payload = @{ frId=$pair[0]; testId=$pair[1] }
    if (Invoke-Mcp 'workflow.requirements.createMapping' $payload) { $ok++ } else { $fail++ }
}
Write-Host ""
Write-Host "============================="
Write-Host "OK/skip: $ok   FAIL: $fail"
Write-Host "============================="
