# SharpNinja.aiUnit

xUnit extension for AI frontier-model regression testing.

`aiUnit` provides:

- **HTTP frontier adapters** for Anthropic Claude, OpenAI-compatible
  (OpenAI / xAI / MAF / Cline), and Google Gemini.
- **CLI strategies** for `claude --print` and `codex exec`.
- **`AiUnitStrategyResolver`** that reads `appsettings.aiunit.json` and
  resolves the active strategy with `AIUNIT_*` env-var overrides.
- **`[AiFact]` / `[AiTheory]`** xUnit attributes that auto-skip when no
  strategy resolves (e.g. no API key + no CLI on PATH).
- **`AiUnitJsonAssertions`** for validating model JSON output.

## Status

Preview - this is the initial extraction from `TruckMate`. Phase 1
(foundation contracts) + Phase 2 (HTTP adapters) ship in
`0.1.0-preview.1`. Phase 3+ (strategy + xUnit attributes) follow.

## License

MIT. See [LICENSE](LICENSE).
