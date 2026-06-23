# Upgrade a workspace to SharpNinja.aiUnit 2.0.0 (xUnit v3)

SharpNinja.aiUnit **2.0.0** is a BREAKING release: it now targets **xUnit v3**
(`xunit.v3`), drops `Xunit.SkippableFact`, and uses the v3 extensibility model.
Any test project that references aiUnit must move to xUnit v3 in the same change.
Usage of the aiUnit attributes themselves is otherwise unchanged.

This document is written so a coding agent (or a developer) can execute it
end-to-end in a consumer workspace. Paste it as a task, or follow it manually.

## 1. Find the affected test projects
- Search every `*.csproj` for `SharpNinja.aiUnit`. Each is a test project that
  must move to xUnit v3. Do them all in one branch.

## 2. Update packages in each affected test .csproj
- `SharpNinja.aiUnit` -> `2.0.0` (use the exact published 2.0.0 version from your feed).
- Replace `xunit` (2.x) with `xunit.v3` `2.0.3`.
- Bump `xunit.runner.visualstudio` to `3.1.5`.
- REMOVE `Xunit.SkippableFact` entirely.
- Keep `Microsoft.NET.Test.Sdk` at `17.12.0` or newer.
- Add `<OutputType>Exe</OutputType>` to the test project's `<PropertyGroup>`
  (xUnit v3 test projects run as executables).

Representative result:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <OutputType>Exe</OutputType>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
  <PackageReference Include="xunit.v3" Version="2.0.3" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  <PackageReference Include="SharpNinja.aiUnit" Version="2.0.0" />
</ItemGroup>
```

## 3. Fix code for the v3 API
- `[AiFact]`, `[AiTheory]`, `[AiCodeReview]`, `[AiPlanReview]`, `[AiProjectReview]`
  usage is UNCHANGED. No edits where they are applied.
- Replace any direct SkippableFact calls:
  - `Skip.If(cond, reason)`    -> `Assert.SkipWhen(cond, reason)`
  - `Skip.IfNot(cond, reason)` -> `Assert.SkipUnless(cond, reason)`
  - unconditional skip         -> `Assert.Skip(reason)`
  - aiUnit's `AiSkip.IfNoStrategy()` still works and is preferred for the
    "no strategy resolved" case.
- Fix removed-namespace usings: `Xunit.Abstractions` no longer exists in v3
  (`ITestOutputHelper`, `IMessageSink`, etc. are now under `Xunit` / `Xunit.v3`).
- Recommended on review-attribute theories (native in v3):
  `[Theory(DisableDiscoveryEnumeration = true)]` so reviews never run at discovery.
- Optional: put review test classes in the shipped serial collection
  `[Collection(AiReviewCollection.Name)]` (`using SharpNinja.AiUnit.Review;`).
  Reviews are already serialized by a process-wide gate inside aiUnit; the
  collection just adds xUnit-idiomatic ordering.

## 4. If the project sets `<TreatWarningsAsErrors>true`
- The xunit.v3 analyzers add `xUnit1051` (prefer `TestContext.Current.CancellationToken`).
  Either thread that token through, or suppress it during the migration:
  `<NoWarn>$(NoWarn);xUnit1051</NoWarn>`.

## 5. For on-demand AI-REVIEW test projects (e.g. an "*.AiReviewTests" harness)
- Add a `.runsettings` next to the test project so a long headless Grok review is
  not killed by the default VSTest host window:

```xml
<RunSettings>
  <RunConfiguration>
    <TestSessionTimeout>2400000</TestSessionTimeout> <!-- >= the bridge timeout, in ms -->
  </RunConfiguration>
</RunSettings>
```

  Run with `dotnet test --settings <that file>`.
- Model selection uses the shared `AIUNIT_MODEL` (the retired `AIUNIT_GROK_MODEL`
  no longer exists). Optionally set `AIUNIT_GROK_DISALLOWED_TOOLS`
  (e.g. `run_terminal_cmd,web_search,web_fetch`) to strip tools from a headless
  Grok review. Grok has no per-run MCP disable; disable an OAuth-prompting
  `mcpserver` plugin in grok config (`grok mcp remove mcpserver`) if it is noisy.

## 6. Validate (must be 100% green before committing)
- `dotnet restore`
- `dotnet build` (0 warnings/errors)
- `dotnet test` for each migrated project: 0 failed (skips are fine only for
  aiUnit's no-strategy auto-skip).

## 7. Commit and sync
- Branch off the default branch, commit with a conventional message
  (`build!: move <project> to aiUnit 2.0.0 / xUnit v3`), and push to `origin`.
- If this workspace uses the MCP session log / requirements process, record the
  turn, decisions, and any new TEST coverage, then open a PR.

## Acceptance criteria
- Every test project that referenced aiUnit now references `xunit.v3` + aiUnit
  `2.0.0`, builds clean, and `dotnet test` is green.
- No remaining references to `Xunit.SkippableFact`, `Skip.If` / `Skip.IfNot`, or
  `Xunit.Abstractions`.
