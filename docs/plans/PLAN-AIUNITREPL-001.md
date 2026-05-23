# PLAN-AIUNITREPL-001: Create aiUnit Configuration REPL

## Goal

Build a .NET tool named `aiunit` that can run as a REPL, one-shot command processor, or terminal UI for discovering aiUnit-enabled projects and managing their strategy configuration.

## Byrd Process Framing

- **Viable**: aiUnit consumers already need repeatable strategy setup across multiple test projects. A tool avoids hand-editing `appsettings.aiunit.json` in every workspace.
- **Valuable**: The tool makes CLI, HTTP provider, global, per-project, restore, and strategy reuse workflows inspectable and testable.
- **Boundary**: Implement one gated slice at a time, keep MCP TODO/session logs current, write tests before implementation, and validate the full prior suite before broadening scope.

## Functional Requirements

- `FR-AIUNITREPL-001`: Discover aiUnit-enabled projects recursively from the current directory or a supplied root.
- `FR-AIUNITREPL-002`: Show discovered projects, active strategy, strategy count, config file path, package/project-reference status, and validation state.
- `FR-AIUNITREPL-003`: Support REPL commands and one-shot commands for `scan`, `list`, `show`, `set-active`, `add-strategy`, `edit-strategy`, `remove-strategy`, `apply-global`, `restore`, `validate`, and `export`.
- `FR-AIUNITREPL-004`: Support a terminal UI mode with screens for workspace overview, project strategy editor, global strategy application, strategy catalog CRUD, and validation/deploy status.
- `FR-AIUNITREPL-005`: Allow zero or more strategies per project while preserving existing project settings during global operations.
- `FR-AIUNITREPL-006`: Catalog strategies from discovered projects and make each available for reuse in selected or all projects.
- `FR-AIUNITREPL-007`: Preserve restorable snapshots before mutations and allow restoring individual project settings.
- `FR-AIUNITREPL-008`: Package and publish the tool as a NuGet .NET tool.
- `FR-AIUNITREPL-009`: Provide SVG wireframes for each TUI screen and capture finished TUI screenshots for each wireframe after implementation.
- `FR-AIUNITREPL-010`: Compare finished TUI screenshots to the wireframes with aiUnit using RiskyStars-style YAML scenarios and strict JSON result validation.

## Technical Requirements

- `TR-AIUNIT-REPL-001`: Place the executable in a separate console project so `SharpNinja.AiUnit` remains a reusable library package.
- `TR-AIUNIT-REPL-002`: Use structured parsers/serializers for JSON configuration instead of string edits.
- `TR-AIUNIT-REPL-003`: Keep discovery deterministic and testable through filesystem abstractions or isolated temp directories.
- `TR-AIUNIT-REPL-004`: Keep command parsing and TUI operations over a shared application service layer.
- `TR-AIUNIT-REPL-005`: Use a proven terminal UI library for TUI rendering unless a repository constraint rules it out during implementation research.
- `TR-AIUNIT-REPL-006`: Store backup snapshots beside the target config in a predictable aiUnit-managed location and never overwrite an unverified backup.
- `TR-AIUNIT-REPL-007`: Wireframe comparison scenarios must include prompt, FR/TR rows, AGENTS-README-FIRST path, actual screenshot path, wireframe path, and result schema.
- `TR-AIUNIT-REPL-008`: aiUnit comparison tests must auto-skip when no strategy resolves, fail invalid JSON, and write comparison result artifacts when enabled.
- `TR-AIUNIT-REPL-009`: Screenshot capture must use a deterministic terminal size and seeded fixture workspace.
- `TR-AIUNIT-REPL-010`: CI must restore, test, pack, and publish the .NET tool package only from the intended branch/version flow.

## Implementation Slices

### Slice 0: Planning and Visual Contract

- Backfill this plan to MCP TODO `PLAN-AIUNITREPL-001`.
- Create initial SVG wireframes for the TUI screens.
- Define the screenshot and aiUnit comparison artifact paths:
  - `docs/wireframes/aiunit-repl/*.svg`
  - `docs/screenshots/aiunit-repl/*.png`
  - `docs/screenshots/aiunit-repl/wireframes/*.png`
  - `tests/SharpNinja.AiUnit.Tests/AiUnitReplWireframeComparisons/*.yaml`
  - `artifacts/aiunit-repl-wireframe-comparisons/*.json`
- Gate: MCP TODO verifies with implementation tasks present; SVGs are well-formed XML.

### Slice 1: Project Shape and CLI Shell

- Add a console project for the tool and reference the library project.
- Add tests that verify project packaging metadata, command entry points, and help text.
- Implement `--help`, `--version`, one-shot command dispatch, and REPL mode selection.
- Gate: focused CLI shell tests pass, solution builds, and no existing tests regress.

### Slice 2: Discovery and Read-Only Inspection

- Add tests for recursive discovery across solution, project, package, and `appsettings.aiunit.json` layouts.
- Implement discovery models and read-only commands: `scan`, `list`, `show`, and `validate`.
- Gate: temp-directory discovery tests cover nested projects, missing configs, malformed configs, duplicate strategy names, and no-project roots.

### Slice 3: Shared Strategy Configuration Services

- Add tests for load/save/merge behavior over aiUnit strategy JSON.
- Implement add/edit/remove/set-active operations through shared services.
- Preserve ordering and unrelated JSON properties.
- Gate: mutation tests prove no unrelated project settings are lost.

### Slice 4: Snapshot and Restore

- Add tests for pre-mutation snapshots, restore by project, restore latest, and failed restore handling.
- Implement snapshot storage and restore commands.
- Gate: restore tests demonstrate exact round-trip behavior and protect corrupt backups.

### Slice 5: Global and Catalog Workflows

- Add tests for cataloging strategies across discovered projects and applying one strategy globally or selectively.
- Implement strategy catalog, apply-global, selected-project apply, dry-run, and conflict reporting.
- Gate: global changes preserve previous settings and provide a dry-run diff before mutation.

### Slice 6: Terminal UI

- Add TUI view-model tests for navigation, state transitions, validation messages, and command binding.
- Implement TUI screens matching the SVG wireframes:
  - Workspace overview.
  - Project strategy editor.
  - Strategy catalog.
  - Validation and deploy.
- Gate: deterministic TUI render tests pass at the target terminal size.

### Slice 7: Screenshot Capture

- Add a deterministic fixture workspace for visual testing.
- Add screenshot capture automation for each finished TUI screen at the agreed terminal size.
- Capture PNG screenshots for every SVG wireframe.
- Gate: screenshots exist, are non-empty, have expected dimensions, and show the requested screen state.

### Slice 8: aiUnit Visual Comparison

- Add RiskyStars-style YAML scenarios for each TUI screen with prompt, FR/TR rows, `AGENTS-README-FIRST.yaml`, screenshot path, wireframe path, and strict result schema.
- Implement `[AiFact]` comparison tests that attach each screenshot and corresponding wireframe to the configured aiUnit strategy.
- Validate strict JSON model output and write per-screen comparison artifacts when enabled.
- Gate: tests skip cleanly without a configured strategy, fail invalid JSON, and pass or produce actionable findings when enabled.

### Slice 9: Packaging and Publish

- Pack the REPL/TUI as a .NET tool with NuGet metadata and CI publish settings.
- Add install/update smoke tests against the packed tool.
- Gate: `dotnet pack` produces the tool package and local `dotnet tool install --add-source` smoke test succeeds.

## Current Wireframe Inventory

- `docs/wireframes/aiunit-repl/01-workspace-overview.svg`
- `docs/wireframes/aiunit-repl/02-project-strategy-editor.svg`
- `docs/wireframes/aiunit-repl/03-strategy-catalog.svg`
- `docs/wireframes/aiunit-repl/04-validation-deploy.svg`

## Validation Commands

- `dotnet test .\SharpNinja.aiUnit.sln`
- `dotnet pack .\src\SharpNinja.AiUnit.Repl\SharpNinja.AiUnit.Repl.csproj --configuration Release`
- `dotnet tool install --add-source .\src\SharpNinja.AiUnit.Repl\bin\Release --tool-path .\artifacts\tooltest SharpNinja.aiUnit.Tool --version 0.5.0-beta`
- `dotnet test .\tests\SharpNinja.AiUnit.Tests\SharpNinja.AiUnit.Tests.csproj --filter "FullyQualifiedName~AiUnitReplWireframeComparison"`

## Current Status

Slice 9 packaging and publish validation is implemented. The REPL now runs a command loop over the existing one-shot commands, the packed .NET tool is smoke-installed from a local package source, and stable version tags produce full-release NuGet package versions before publish. Direct `set-active`, `add-strategy`, `edit-strategy`, `remove-strategy`, and `export` command aliases remain a follow-up if FR-AIUNITREPL-003 still requires those exact command names.
