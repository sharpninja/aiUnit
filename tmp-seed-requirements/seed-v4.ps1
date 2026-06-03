# Direct-REST seeder for AIUNITDESKTOP requirements.
# Bypasses the broken plugin write path (see debug-plugin-exit1.md).
# Reads API key fresh from AGENTS-README-FIRST.yaml.

$ErrorActionPreference = 'Continue'

# Resolve fresh API key from marker file
$marker = 'F:\GitHub\aiUnit\AGENTS-README-FIRST.yaml'
$apiKeyLine = (Select-String -Path $marker -Pattern '^apiKey:\s*(\S+)' | Select-Object -First 1).Matches[0].Groups[1].Value
if (-not $apiKeyLine) { throw "Could not read apiKey from $marker" }
$baseUrl = 'http://PAYTON-LEGION2:7147'
$headers = @{ 'X-Api-Key' = $apiKeyLine; 'Content-Type' = 'application/json' }

function Invoke-Rest([string]$method, [string]$path, $body, [string]$label) {
    $url = "$baseUrl$path"
    $start = Get-Date
    try {
        if ($body) {
            $resp = Invoke-RestMethod -Uri $url -Method $method -Headers $headers -Body ($body | ConvertTo-Json -Depth 8 -Compress)
        } else {
            $resp = Invoke-RestMethod -Uri $url -Method $method -Headers $headers
        }
        $dur = [int]((Get-Date) - $start).TotalMilliseconds
        Write-Host "  [ok] $method $path $label (${dur}ms)"
        return $resp
    } catch {
        $dur = [int]((Get-Date) - $start).TotalMilliseconds
        $err = $_.Exception.Response
        $msg = $_.Exception.Message
        try {
            $stream = $err.GetResponseStream(); $stream.Position = 0
            $sr = New-Object IO.StreamReader $stream
            $body = $sr.ReadToEnd()
            if ($body) { $msg = $body }
        } catch {}
        Write-Host "  [FAIL] $method $path $label (${dur}ms) -> $msg"
        return $null
    }
}

# ---------------- FR records ----------------
$frs = @(
    @{ id='FR-AIUNITDESKTOP-001'; title='Scenarios load from configurable folders';
       body='Desktop app loads wireframe scenarios from three user-configurable folder paths (scenarios YAML, wireframe SVG, actual screenshot PNG) instead of marker-walking the repo. Loader resolves YAML-relative paths against the configured wireframes and screenshots folders, accepts absolute paths verbatim, and surfaces a per-scenario error placeholder when a referenced image is missing.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-002'; title='First-run folder pickers and persisted settings';
       body='On first launch the Settings flyout opens automatically and asks the user to pick three folders (scenarios YAML, wireframe SVG, actual screenshot PNG). Folder pickers use Avalonia StorageProvider OpenFolderPickerAsync. Settings persist to a JSON file under user-profile AppData. Subsequent launches skip the flyout and load directly. A reload-scenarios action re-runs the loader.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-003'; title='Agent selection from strategy config with built-in grok fallback';
       body='A toolbar ComboBox lets the user pick the agent CLI to host in the embedded terminal. Items are populated from AiUnitStrategyLoader TryLoad filtered to Kind cli (claude-code, codex). Grok is provided as a built-in fallback even when no matching strategy exists in appsettings aiunit json. Selecting an agent disposes any running subprocess gracefully (2 second SIGTERM grace) and launches the new one.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-004'; title='Embedded native terminal hosts agent CLIs';
       body='Middle column of the main window hosts a native Avalonia terminal (Iciclecreek Terminal TerminalControl) that runs the selected agent CLI under a real PTY (ConPTY on Windows, forkpty on Unix). The terminal renders VT100 ANSI sequences (colors, cursor movement, alternate buffer) and supports keyboard input. Required because claude and codex switch to non-interactive mode when stdin is not a TTY.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-005'; title='Three-column layout with top scenario nav';
       body='Main window shows a top nav bar listing scenarios in numeric-prefix order with the active item highlighted, three resizable body columns (wireframe SVG left, embedded terminal middle, actual screenshot PNG right), and a bottom status bar with screen id, agent name, process status, and PID. Clicking a scenario in the nav refreshes both image panels.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-006'; title='Paste scenario YAML to agent stdin';
       body='A toolbar button (Segoe MDL2 Send glyph) writes the current scenario ModelPayloadYaml (raw YAML plus redacted AGENTS-README content) followed by newline to the terminal stdin. This primes the agent with the scenario context for human-in-the-loop review.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-007'; title='Undockable chat terminal window';
       body='A toolbar toggle moves the embedded terminal out of the main window into a separate UndockedChatWindow. The same ChatTerminalViewModel instance is the DataContext in both locations so the PTY stream and transcript continue uninterrupted across dock and undock cycles. Closing the undocked window redocks the terminal back into the middle column.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-008'; title='Undocked window position remembered per monitor';
       body='AppSettings persists the undocked window bounds (X, Y, width, height, screen display name). On re-undock the window is restored to its prior monitor and position. If the prior monitor is no longer attached, the window centers on the primary screen and bounds are sanity-clamped to remain on-screen.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-009'; title='Per-scenario human review verdict persisted as sidecar';
       body='A VerdictPanel allows the reviewer to record approved, rejected, or needs-changes plus free-text notes per scenario. Verdicts are written to a sidecar file named screenId review yaml next to the scenario YAML. The loader merges any sidecar into the in-memory WireframeScenario. Save writes the file. Clear deletes it. Status bar shows the current verdict.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-010'; title='Agent result JSON detected and saved to artifacts';
       body='The chat view watches the transcript for a JSON object matching the scenario resultSchema (fenced json block or trailing balanced-brace object). When detected, a non-modal toast offers Save, Save and Score, or Dismiss. Save writes the JSON to artifacts aiunit-desktop-reviews under the aiUnit repo root when cwd is inside it, otherwise under user-profile AppData fallback.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-011'; title='Saved JSON scored against scenario resultSchema';
       body='Save and Score additionally validates the captured JSON against the scenario embedded resultSchema using JsonSchema Net and writes a sibling score json file summarizing pass and fail per schema rule and an overall verdict.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-012'; title='Macro replay across all scenarios with summary';
       body='A Run-all toolbar button iterates scenarios in numeric-prefix order: selects each, pastes ModelPayloadYaml, waits for the agent to emit JSON (poll 250 ms with configurable per-scenario timeout, default 5 minutes), saves and scores the result under artifacts aiunit-desktop-reviews per-run folder. A MacroRunSummaryWindow shows total scenarios scored and passed at end and offers Open Folder. Cancellable mid-run.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-013'; title='Cross-platform icon kit Segoe MDL2 on Windows SVG on others';
       body='An IconKit maps logical icon names (Send, Settings, Play, FolderOpen, NewWindow, BackToWindow, Refresh, Stop, ChevronLeft, ChevronRight) to the appropriate visual: Segoe MDL2 Assets codepoint on Windows or a Projektanker Icons Avalonia SVG identifier on Linux and macOS. Detection at startup binds the right resource template so XAML stays platform-neutral.';
       priority='medium' },
    @{ id='FR-AIUNITDESKTOP-014'; title='Distribution as dotnet tool aiunit-review with probe-exit smoke';
       body='The Desktop app ships as a packable dotnet tool. csproj has PackAsTool true, ToolCommandName aiunit-review, PackageId SharpNinja aiUnit Desktop Tool. Users install with dotnet tool install global and run aiunit-review. The app honors a probe-exit flag that returns 0 immediately without opening a window so CI can smoke-test the package on headless agents.';
       priority='medium' }
)

# ---------------- TR records ----------------
$trs = @(
    @{ id='TR-AIUNITDESKTOP-LOADER-001'; title='WireframeScenario record and folder-driven loader'; body='Public records SharpNinja AiUnit Scenarios WireframeScenario and WireframeRequirement added to the core library. Desktop project provides WireframeScenarioLoader LoadAsync taking three folder paths returning a list of WireframeScenario. Relative paths in YAML resolved against the configured folders. Absolute paths used verbatim. Missing image files yield an error placeholder per scenario not an exception.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-SETTINGS-001'; title='AppSettings JSON persistence and SettingsFlyout pickers'; body='AppSettings is a record holding ScenariosFolder, WireframesFolder, ScreenshotsFolder, LastAgent, UndockedWindowBounds. SettingsService loads and saves a JSON file at user-profile AppData with platform-neutral fallback. SettingsFlyout XAML has three folder-picker rows backed by TopLevel GetTopLevel StorageProvider OpenFolderPickerAsync.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-AGENT-001'; title='AgentCommandResolver reuses AiUnitStrategyLoader'; body='AgentCommandResolver calls AiUnitStrategyLoader TryLoad on startup, filters config Strategies to Kind cli, and returns a list of AgentCommand records (Name, Command, Args, WorkingDir, Env). Grok is appended as a built-in entry when no matching strategy exists.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-TERMINAL-001'; title='ITerminal abstraction over Iciclecreek terminal control'; body='ITerminal interface exposes Launch (workdir, exe, args), Kill, WriteInput (string), event ProcessExited (exitCode), Pid, IsRunning. Real adapter wraps Iciclecreek Terminal TerminalControl (LaunchProcess, Kill, Pid, ProcessExited). Paste-input API confirmed by pre-phase-4 spike against the loaded assembly with fallbacks documented in plan.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-UI-001'; title='MainWindow 3-column grid and scenario nav'; body='MainWindow axaml uses a Grid with RowDefinitions Auto-star-Auto and ColumnDefinitions star-Auto-2star-Auto-star with GridSplitters. Top nav ItemsControl binds ScenarioListViewModel Scenarios sorted by NumericPrefix with active scenario tagged Classes active. Wireframe panel renders SVG via Avalonia Svg Skia Svg. Screenshot panel uses Image with Stretch Uniform.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-AGENT-002'; title='Paste-YAML command in ChatTerminalViewModel'; body='ChatTerminalViewModel PasteScenarioCommand reads the active scenarios ModelPayloadYaml from the bound ScenarioViewModel and calls ITerminal WriteInput with the model payload plus newline. Disabled when no agent is running or no scenario selected.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-UNDOCK-001'; title='UndockedChatWindow shares VM with main window'; body='ChatTerminalViewModel IsDocked observable property controls the swap. MainWindow column 2 width is bound to IsDocked via BoolToGridLengthConverter (0 when undocked, 2 star when docked) and splitters hidden. UndockedChatWindow DataContext is the same ChatTerminalViewModel instance so the live PTY stream is uninterrupted. Closing event sets IsDocked true.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-SETTINGS-002'; title='UndockedWindowBounds persistence with multi-monitor restore'; body='AppSettings UndockedWindowBounds with X, Y, Width, Height, ScreenName captured on UndockedChatWindow Closing via Screens ScreenFromVisual. On re-undock restored when screen still present in MainWindow Screens All. Otherwise centered on primary screen. Bounds sanity-clamped to keep window on-screen.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-VERDICT-001'; title='VerdictStore sidecar YAML and loader merge'; body='VerdictStore Read (scenariosFolder, screenId), Write (scenariosFolder, screenId, verdict), Clear (scenariosFolder, screenId). Verdict shape includes verdict (approved or rejected or needs-changes), reviewer, reviewedAt UTC, notes. WireframeScenarioLoader merges any sibling screenId review yaml into scenario HumanReview at load time.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-CAPTURE-001'; title='ResultCaptureService detects and saves transcript JSON'; body='ResultCaptureService TryDetect (transcript slice) returns found and json using regex on fenced json blocks plus a balanced-brace trailing-object fallback. SaveAsync (json, artifactsRoot, screenId, utc) writes screenId json under artifacts aiunit-desktop-reviews. Artifacts root resolved to aiUnit repo root when cwd is inside it else AppData fallback.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-SCORE-001'; title='ResultScoringService validates JSON against resultSchema'; body='ResultScoringService ScoreAsync (capturedJson, resultSchemaJson) returns ScoreResult with passed flag and list of SchemaViolation. Uses JsonSchema Net for validation. Writes sibling score json with per-rule pass and fail summary and overall verdict.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-MACRO-001'; title='MacroRunner orchestrates run-all with cancellation'; body='MacroRunner RunAsync (scenarios, ITerminal, IResultCapture, IResultScoring, CancellationToken) iterates scenarios in order. Paste ModelPayloadYaml, poll 250 ms for captured JSON up to per-scenario timeout (default 5 minutes, settings-configurable), save and score, advance. Emits per-scenario progress events. On cancel lets current scenario finish then exits. Writes summary json roll-up.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-ICON-001'; title='IconKit Windows vs cross-platform mapping'; body='IconKit static maps logical name to a pair of segoeCodepoint and projektankerId. At startup OperatingSystem IsWindows binds the global IconStyle resource: a TextBlock template using FontFamily Segoe MDL2 Assets on Windows or an Icon template using Projektanker Icons Avalonia on others. XAML buttons reference Static Resource icon name.'; priority='medium' },
    @{ id='TR-AIUNITDESKTOP-PACKAGE-001'; title='Dotnet tool packaging with probe-exit smoke'; body='csproj sets OutputType Exe, PackAsTool true, ToolCommandName aiunit-review, PackageId SharpNinja aiUnit Desktop Tool, GenerateDocumentationFile true, Title, Description, PackageTags. Program Main returns 0 immediately when args contain probe-exit without instantiating MainWindow. Azure pipeline Smoke test step extended to install the tool and run probe-exit.'; priority='medium' }
)

# ---------------- TEST records ----------------
$tests = @(
    @{ id='TEST-AIUNITDESKTOP-001'; title='WireframeScenarioLoaderTests'; condition='Cover loading 4 scenarios from a configured folder set, relative-path resolution against wireframes and screenshots folders, absolute-path passthrough, missing-file placeholder behavior, and sidecar verdict merge. Uses an in-memory file system fake for mocks-first phase before real I O.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-002'; title='AppSettingsTests'; condition='Cover load and save JSON round-trip including UndockedWindowBounds, missing-file yields default settings, malformed-file recovery. Settings file path resolves correctly per platform.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-003'; title='AgentCommandResolverTests'; condition='Cover claude and codex resolution from a synthetic strategy config, grok built-in fallback when no matching strategy, ordering of returned AgentCommand list.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-004'; title='AgentProcessHostTests'; condition='Cover Launch, Kill, WriteInput, ProcessExited contract on the ITerminal abstraction with a fake terminal. Includes graceful kill on agent change (2 second SIGTERM grace) and process restart.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-005'; title='ScenarioListViewModelTests'; condition='Cover scenario ordering by numeric prefix, selection raising PropertyChanged, active-item highlight flag, and re-load after settings change.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-006'; title='ChatTerminalViewModelTests'; condition='Cover PasteScenarioCommand calls ITerminal WriteInput exactly once with the active scenarios ModelPayloadYaml plus newline. CanExecute is false when no agent is running or no scenario selected.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-007'; title='UndockViewModelAndBoundsTests'; condition='Cover dock toggle command IsDocked round-trip, UndockedWindowBounds round-trip in AppSettings, on-screen clamp when prior monitor is missing, and DataContext sharing across dock and undock.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-008'; title='VerdictStoreTests'; condition='Cover sidecar write and read round-trip for approved, rejected, and needs-changes verdicts with notes. Clear deletes the file. Loader merges sidecar into scenario HumanReview.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-009'; title='ResultCaptureServiceTests'; condition='Cover regex-detect a fenced json block in a synthetic transcript, balanced-brace trailing object fallback, ignore non-JSON content, write file under artifacts aiunit-desktop-reviews with correct UTC formatting.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-010'; title='ResultScoringServiceTests'; condition='Cover known-good JSON passes resultSchema validation, known-bad JSON yields per-rule violations, score json side-output structure.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-011'; title='MacroRunnerTests'; condition='Cover ordered iteration across 4 scenarios using a fake ITerminal and fake IResultCapture, per-scenario timeout enforcement, cancellation behavior (current scenario finishes then exits), summary json roll-up structure.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-012'; title='IconKitTests'; condition='Cover Windows OS path returns Segoe MDL2 codepoint mapping for all logical icon names, non-Windows OS path returns Projektanker SVG identifier mapping, no missing-name entries.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-013'; title='ProbeExitTests'; condition='Cover Program Main returning 0 immediately when probe-exit flag is in args, without instantiating MainWindow. Verifies the headless CI smoke contract.'; priority='medium' },
    @{ id='TEST-AIUNITDESKTOP-014'; title='IntegrationManualValidation'; condition='Manual end-to-end validation: full macro replay across all 4 scenarios with a real agent (claude or codex or grok), verdict persistence across relaunch, undock and redock on a second monitor. Documented in test plan, not auto-executed.'; priority='medium' }
)

# ---------------- FR -> {trIds, testIds} mappings ----------------
$frMappings = @(
    @{ frId='FR-AIUNITDESKTOP-001'; trIds=@('TR-AIUNITDESKTOP-LOADER-001');     testIds=@('TEST-AIUNITDESKTOP-001','TEST-AIUNITDESKTOP-014') },
    @{ frId='FR-AIUNITDESKTOP-002'; trIds=@('TR-AIUNITDESKTOP-SETTINGS-001');   testIds=@('TEST-AIUNITDESKTOP-002') },
    @{ frId='FR-AIUNITDESKTOP-003'; trIds=@('TR-AIUNITDESKTOP-AGENT-001');      testIds=@('TEST-AIUNITDESKTOP-003') },
    @{ frId='FR-AIUNITDESKTOP-004'; trIds=@('TR-AIUNITDESKTOP-TERMINAL-001');   testIds=@('TEST-AIUNITDESKTOP-004') },
    @{ frId='FR-AIUNITDESKTOP-005'; trIds=@('TR-AIUNITDESKTOP-UI-001');         testIds=@('TEST-AIUNITDESKTOP-005') },
    @{ frId='FR-AIUNITDESKTOP-006'; trIds=@('TR-AIUNITDESKTOP-AGENT-002');      testIds=@('TEST-AIUNITDESKTOP-006') },
    @{ frId='FR-AIUNITDESKTOP-007'; trIds=@('TR-AIUNITDESKTOP-UNDOCK-001');     testIds=@('TEST-AIUNITDESKTOP-007','TEST-AIUNITDESKTOP-014') },
    @{ frId='FR-AIUNITDESKTOP-008'; trIds=@('TR-AIUNITDESKTOP-SETTINGS-002');   testIds=@('TEST-AIUNITDESKTOP-007') },
    @{ frId='FR-AIUNITDESKTOP-009'; trIds=@('TR-AIUNITDESKTOP-VERDICT-001');    testIds=@('TEST-AIUNITDESKTOP-008') },
    @{ frId='FR-AIUNITDESKTOP-010'; trIds=@('TR-AIUNITDESKTOP-CAPTURE-001');    testIds=@('TEST-AIUNITDESKTOP-009') },
    @{ frId='FR-AIUNITDESKTOP-011'; trIds=@('TR-AIUNITDESKTOP-SCORE-001');      testIds=@('TEST-AIUNITDESKTOP-010') },
    @{ frId='FR-AIUNITDESKTOP-012'; trIds=@('TR-AIUNITDESKTOP-MACRO-001');      testIds=@('TEST-AIUNITDESKTOP-011','TEST-AIUNITDESKTOP-014') },
    @{ frId='FR-AIUNITDESKTOP-013'; trIds=@('TR-AIUNITDESKTOP-ICON-001');       testIds=@('TEST-AIUNITDESKTOP-012') },
    @{ frId='FR-AIUNITDESKTOP-014'; trIds=@('TR-AIUNITDESKTOP-PACKAGE-001');    testIds=@('TEST-AIUNITDESKTOP-013') }
)

# ---------------- Execute ----------------
Write-Host "=== POST FR batch (14) ==="
$null = Invoke-Rest 'POST' '/mcpserver/requirements/fr/batch' @{ records=$frs } 'FRx14'

Write-Host "=== POST TR batch (14) ==="
$null = Invoke-Rest 'POST' '/mcpserver/requirements/tr/batch' @{ records=$trs } 'TRx14'

Write-Host "=== POST TEST batch (14) ==="
$null = Invoke-Rest 'POST' '/mcpserver/requirements/test/batch' @{ records=$tests } 'TESTx14'

Write-Host "=== PUT mappings (14 FRs) ==="
foreach ($m in $frMappings) {
    $null = Invoke-Rest 'PUT' "/mcpserver/requirements/mapping/$($m.frId)" @{ trIds=$m.trIds; testIds=$m.testIds } $m.frId
}

Write-Host ""
Write-Host "=== Verify counts ==="
$frCount = (Invoke-Rest 'GET' '/mcpserver/requirements/fr' $null 'list').Count
$trCount = (Invoke-Rest 'GET' '/mcpserver/requirements/tr' $null 'list').Count
$testCount = (Invoke-Rest 'GET' '/mcpserver/requirements/test' $null 'list').Count
Write-Host "Workspace totals: FR=$frCount TR=$trCount TEST=$testCount"

# Count AIUNITDESKTOP only
$ads = Invoke-Rest 'GET' '/mcpserver/requirements/fr' $null 'list-all-fr'
$adsFr = @($ads | Where-Object { $_.id -like 'FR-AIUNITDESKTOP-*' }).Count
Write-Host "AIUNITDESKTOP FR count: $adsFr (expect 14)"
