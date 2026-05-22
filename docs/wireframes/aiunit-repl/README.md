# aiUnit REPL TUI Wireframes

These SVGs define the first visual contract for `PLAN-AIUNITREPL-001`.

The future implementation must capture finished TUI screenshots to `docs/screenshots/aiunit-repl/` and compare each screenshot to its matching SVG through aiUnit using RiskyStars-style YAML scenarios and strict JSON result validation.

## Screens

- `01-workspace-overview.svg`: recursive project discovery and global state.
- `02-project-strategy-editor.svg`: per-project strategy CRUD and restore.
- `03-strategy-catalog.svg`: strategy reuse across discovered projects.
- `04-validation-deploy.svg`: validation, package/tool deployment, and publish readiness.

## Target Terminal Contract

- Terminal size: 120 columns by 36 rows.
- Theme: dark neutral terminal surface, blue focus, green success, amber warning, red error.
- Layout: persistent header, left navigation rail, main workspace, right detail/action panel, bottom command/status bar.
