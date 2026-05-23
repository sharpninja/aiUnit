# aiUnit

aiUnit is an xUnit extension for AI frontier-model regression tests,
AI-assisted review tests, and workspace strategy management.

## User Documentation

- [[Configuration]]
- [[Review Attributes]]
- [[CLI and TUI|CLI-and-TUI]]

## Requirements

- [[Requirements]]
- [[Functional Requirements]]
- [[Technical Requirements]]
- [[Testing Requirements]]
- [[Traceability Mapping|TR-per-FR-Mapping]]
- [[Requirements Matrix]]

## Quick Start

1. Add `SharpNinja.aiUnit` to the test project.
2. Copy `samples/appsettings.aiunit.json` from the source repository into the
   test project output as `appsettings.aiunit.json`.
3. Pick an active strategy with `AiUnit:ActiveStrategy` or `AIUNIT_STRATEGY`.
4. Run `dotnet test`.

CLI strategies launch local tools such as Codex, Claude Code, or Copilot.
HTTP strategies call Anthropic, OpenAI-compatible, xAI, MAF, or Gemini APIs
directly.
