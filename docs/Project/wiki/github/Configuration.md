# Configuration

aiUnit reads `appsettings.aiunit.json` from the test process output directory.
`AIUNIT_STRATEGY` can override `AiUnit:ActiveStrategy` without editing the file.

## Strategy Kinds

- `cli`: launches the executable configured by `Command`. The executable owns
  auth. `ApiKeyEnvVar` is not used for `cli` strategies.
- `anthropic`: calls the Anthropic Messages API.
- `openai-compatible`: calls an OpenAI-compatible `/v1/chat/completions`
  endpoint, including OpenAI, xAI, MAF, and similar gateways.
- `gemini`: calls the Google Gemini Generative Language API.

## Environment Overrides

- `AIUNIT_STRATEGY`
- `AIUNIT_KIND`
- `AIUNIT_BASE_URL`
- `AIUNIT_MODEL`
- `AIUNIT_COMMAND`
- `AIUNIT_API_KEY`
- `AIUNIT_TIMEOUT_SECONDS`
- `AIUNIT_TEMPERATURE`

HTTP strategies use their configured `ApiKeyEnvVar` first and fall back to
`AIUNIT_API_KEY`.

CLI strategies use the process environment of the launched executable. Configure
the tool before running tests:

```powershell
codex login
$env:ANTHROPIC_API_KEY = "<anthropic-api-key>"
$env:COPILOT_API_KEY = "<copilot-api-key>"
```

## Corrected Examples

```json
{
  "AiUnit": {
    "ActiveStrategy": "codex-subscription",
    "Strategies": {
      "codex-subscription": {
        "Kind": "cli",
        "Command": "codex",
        "Model": "(cli-managed)",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Codex CLI using the logged-in subscription."
      },
      "claude-code-opus": {
        "Kind": "cli",
        "Command": "claude",
        "Model": "claude-opus-4-5",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Claude Code CLI using Opus. Configure ANTHROPIC_API_KEY or Claude CLI auth in the process environment."
      },
      "copilot-gemini": {
        "Kind": "cli",
        "Command": "copilot",
        "Model": "gemini-2.5-pro",
        "TimeoutSeconds": 900,
        "Temperature": 0.0,
        "Description": "Copilot CLI configured for Gemini. Configure the required Copilot CLI auth or API key in the process environment."
      },
      "maf-grok": {
        "Kind": "openai-compatible",
        "BaseUrl": "https://api.x.ai",
        "Model": "grok-4",
        "ApiKeyEnvVar": "XAI_API_KEY",
        "TimeoutSeconds": 1800,
        "Temperature": 0.0,
        "Description": "xAI Grok through the OpenAI-compatible API. Set XAI_API_KEY before activating."
      }
    }
  }
}
```

`claude-code-opus` and `copilot-gemini` are external CLI launches. Do not model
them as MAF/OpenAI-compatible HTTP strategies unless you are intentionally using
an HTTP gateway.
