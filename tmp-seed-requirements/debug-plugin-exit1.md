# Debug: `workflow.requirements.createFr` (and `createTr`, `createTest`) returns exit 1 after successful insert

**Run this prompt in a session rooted at `F:\GitHub\McpServer` (the MCP server + plugin source repo).**

## Symptom

Plugin call via `mcpserver-claude-code-plugin` succeeds on the server (record lands in DB with fresh `createdAt` timestamp), but the plugin wrapper script throws:

```
Exception: F:\GitHub\mcpserver-claude-code-plugin\lib\Invoke-ClaudeMcpPlugin.ps1:172
Line |
 172 |          throw "Plugin command failed with exit code $($process.ExitCo .
     |          ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | Plugin command failed with exit code 1.
```

Caller sees only the throw; no server response body surfaces. Every `createFr` / `createTr` / `createTest` call in a bulk seeder run reported `[FAIL]`, yet `getFr <id>` immediately afterwards returned the new record with `createdAt` = a few seconds before the failure.

## Repro

From any workspace with `pwsh -NoProfile`:

```pwsh
$env:PSModulePath = "F:\GitHub\McpServer\tools\powershell;$env:PSModulePath"  # ensure McpRepl module is discoverable
$plugin = "F:\GitHub\mcpserver-claude-code-plugin\lib\Invoke-ClaudeMcpPlugin.ps1"
$params = "id: FR-DEBUG-001`ntitle: Debug record`ndescription: Probe`narea: DEBUG`npriority: medium"
pwsh -NoProfile -NonInteractive -NoLogo -File $plugin -Command Invoke -Method workflow.requirements.createFr -Params $params 2>&1
```

Expected: exit 0, YAML response containing `result.success: true` and `result.item.id`.
Actual: exit 1 with the throw above; **record nevertheless persists** (verify with `workflow.requirements.getFr id: FR-DEBUG-001`).

## Where to look

1. **`F:\GitHub\mcpserver-claude-code-plugin\lib\Invoke-ClaudeMcpPlugin.ps1`** line ~140-178 — the bash subprocess invocation. The bash side may exit non-zero AFTER the POST succeeded:
   - Capture both `stdout` and `stderr` of the bash subprocess and dump them before the throw. Currently only stderr is echoed; stdout (with the actual server response) is suppressed when `$process.ExitCode -ne 0`.
2. **`F:\GitHub\mcpserver-claude-code-plugin\lib\repl-invoke.sh`** handlers for `createFr` / `createTr` / `createTest` (~line 1338-1378). Look for any post-POST step (response parse, schema validation, JSON->YAML re-render) that could fail and propagate exit≠0 even when the HTTP call returned 2xx.
3. **`F:\GitHub\McpServer\tools\powershell\McpRepl\McpRepl.psm1`** (and any Send-MCP / Invoke-McpServer cmdlet) — confirm the cmdlet uses the HTTP status code, not the response body shape, to drive its exit decision.
4. **Server endpoint POST `/mcpserver/requirements/fr`** — does it return `201 Created` with a body shape the client doesn't expect? Compare an `updateFr` response (which works) to a `createFr` response (which doesn't).

## What's already known

- `getFr`, `updateFr`, `listFr`, `listMappings` all return exit 0 with usable YAML in the same session, same plugin, same PSModulePath.
- The `createFr` probe shape that returned `success: true` in an earlier session matches the script. So the response shape changed, or a new post-POST step was added.
- Workaround in the seeder script today: treat any record that the next `getFr` confirms as "actually created" regardless of the throw. Hacky — fix the wrapper instead.

## Asks

1. Reproduce locally (the repro above takes 5 seconds).
2. Patch `Invoke-ClaudeMcpPlugin.ps1` to surface stdout even on non-zero exit (so callers can see the actual response).
3. Identify and fix whatever post-success step in the create path is exiting non-zero. Likely a response-shape mismatch between server and the bash response parser.
4. Add a unit test in the plugin repo: round-trip create -> get for FR / TR / TEST returns exit 0 on both sides.
5. Report back the root cause and the commit that fixes it.
