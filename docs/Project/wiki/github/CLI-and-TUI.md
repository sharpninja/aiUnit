# CLI and TUI

The `aiunit` tool manages strategy configuration across a workspace.

```powershell
aiunit repl --workspace F:\GitHub\aiUnit
aiunit tui overview --workspace F:\GitHub\aiUnit
aiunit scan --workspace F:\GitHub\aiUnit
aiunit list --workspace F:\GitHub\aiUnit
aiunit catalog --workspace F:\GitHub\aiUnit
aiunit validate --workspace F:\GitHub\aiUnit
aiunit show <project> --workspace F:\GitHub\aiUnit
aiunit apply <strategy> --project <project> --dry-run --force --workspace F:\GitHub\aiUnit
aiunit apply-global <strategy> --dry-run --force --workspace F:\GitHub\aiUnit
aiunit restore <project> --snapshot <path> --workspace F:\GitHub\aiUnit
aiunit --version
aiunit --help
```

## Modes

- `repl`: interactive strategy management.
- `tui`: terminal UI views for overview, projects, catalog, and validation.
- `scan`: discover projects and aiUnit config files.
- `list`: list discovered projects.
- `show`: inspect one project.
- `catalog`: list available strategies across the workspace.
- `apply`: apply one strategy to one project.
- `apply-global`: apply one strategy across projects.
- `restore`: restore a saved config snapshot.
- `validate`: validate discovered configuration.

Use `--dry-run` before applying changes when checking how a strategy migration
will affect a workspace.
