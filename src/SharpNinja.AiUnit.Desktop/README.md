# aiunit-review (Avalonia 12 dotnet-tool)

Avalonia desktop application (runnable as `dotnet tool`) that **finds aiUnit tests** (the wireframe-to-screenshot comparison YAML scenarios) and provides a visual reviewer:

- **Left**: Wireframe (PNG; scrollable) baseline
- **Middle**: Chat/terminal (pwsh in CWD using the *exact same Iciclecreek.Avalonia.Terminal control* as the sibling Avalonia.RemoteControl tool; see its ToolPanels/TerminalPanel for integration)
- **Right**: Actual screenshot PNG (scrollable)

Panels are equal width (star sizing). Top bar has dropdown for image scaling: None (1:1), Stretch (distort), Fit (uniform), Fill (cover). Selection applies live to both images (default: Fit). Scrollbars auto-appear for panning when image exceeds panel (e.g. None + large asset).

Bottom bar now supports specifying a verdict for the *current* scenario: choose approved / rejected / needs-changes, optional notes, then "Save Verdict". This writes a sidecar `<screenId>.review.yaml` (next to the scenario YAML) using the same convention the loader merges on startup. Use the icon navigation buttons (chevron left/right) or keyboard arrows (if extended) to switch scenarios (updates images + pre-populates any prior saved verdict). Buttons are auto-disabled at ends. Status text reflects the current verdict.

Hosts and exposes Avalonia.RemoteControl debugging services (per revision) so the comparison UI itself (and potentially other Avalonia apps) can be live-inspected via the sibling client/tool/MCP.

## Usage (as dotnet-tool)
dotnet tool install -g SharpNinja.AiUnit.Desktop.Tool
aiunit-review

On first run: settings flyout for the 3 folders (scenarios YAMLs = the aiUnit tests, wireframes, screenshots).

See the approved plan (in .grok session) for full Byrd v4 TDD slices, ACs, hosting integration, and reuse of sibling files.

Built following the detailed specs in docs/Project/wiki (FR-AIUNITDESKTOP-*, TR-*, TEST-*) + user revisions for layout/terminal/pwsh/CWD/hosting.

## Development
The project is the Avalonia 12 (targeting per query/sibling 12.0.3 when packages available) exe + tool pack. Loader in Services finds the tests. Core records in ../SharpNinja.AiUnit/Scenarios for sharing.

See plan.md for the complete implementation roadmap.
