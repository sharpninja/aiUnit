using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.AzurePipelines;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;

[ShutdownDotNetAfterServerBuild]
// [AzurePipelines(...)]  // Commented to prevent CI config generation during local runs.
// The attribute can be re-enabled when needed for pipeline code-gen.
class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build")]
    readonly string Configuration = "Release";

    [Parameter("Optional explicit version to use for packaging (overrides GitVersion for release tags)")]
    readonly string Version;

    [Solution("SharpNinja.aiUnit.sln")] readonly Solution Solution;
    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath NuGetOutputDirectory => ArtifactsDirectory / "nupkg";

    // Tool projects
    // Use explicit paths for reliability (Solution model had injection issues in this env)
    AbsolutePath AiunitReplProjectPath => SourceDirectory / "SharpNinja.AiUnit.Repl" / "SharpNinja.AiUnit.Repl.csproj";
    AbsolutePath AiunitDesktopProjectPath => SourceDirectory / "SharpNinja.AiUnit.Desktop" / "SharpNinja.AiUnit.Desktop.csproj";

    // Package IDs
    const string AiunitToolPackageId = "SharpNinja.aiUnit.Tool";
    const string AiunitReviewToolPackageId = "SharpNinja.AiUnit.Desktop.Tool";

    // Local tool redeploy output (matches previous manual usage)
    AbsolutePath LocalToolSource => ArtifactsDirectory;

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetTasks.DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.minimal));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                .SetDeterministic(true)
                .SetContinuousIntegrationBuild(IsServerBuild)
                // Always honor GitVersion for assembly metadata on all built artifacts.
                // This ensures the "bumping" (major/minor/patch/pre-release calculation per GitVersion.yml)
                // is applied to DLLs, etc.
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                // Only force a top-level Version override if explicitly provided (e.g. release tag from CI).
                // Do NOT pass it unconditionally — that would skip GitVersion's version calculation/bumping
                // for normal development and CI builds on main.
                .When(!string.IsNullOrWhiteSpace(Version), _ => _
                    .SetVersion(Version)));
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTasks.DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoRestore(true)
                .SetNoBuild(true)
                .SetLoggers("trx")
                .SetResultsDirectory(ArtifactsDirectory / "test-results")
                .SetVerbosity(DotNetVerbosity.normal));
        });

    // Individual pack targets for the two dotnet tools
    Target PackAiunit => _ => _
        .DependsOn(Compile)
        .Produces(NuGetOutputDirectory / $"{AiunitToolPackageId}.*.nupkg")
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(s =>
            {
                s = s
                    .SetProject(AiunitReplProjectPath)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(NuGetOutputDirectory)
                    .SetNoBuild(true)
                    .SetNoRestore(true)
                    .SetProperty("ContinuousIntegrationBuild", IsServerBuild)
                    .SetVerbosity(DotNetVerbosity.minimal);

                // Always honor GitVersion semantic versions for the package metadata on artifacts.
                s = s
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion);

                // Always set the NuGet package version from GitVersion (unless an explicit --version
                // override was passed for a stable tag release). This ensures the produced .nupkg
                // filename and installed tool version always reflect the current GitVersion calculation
                // (including any bumping from GitVersion.yml + commits). Previously the pack could
                // produce the same "0.11.0-beta.2" nupkg name forever.
                var pkgVer = !string.IsNullOrWhiteSpace(Version) ? Version : GitVersion.NuGetVersionV2;
                s = s.SetVersion(pkgVer);

                return s;
            });
        });

    Target PackAiunitReview => _ => _
        .DependsOn(Compile)
        .Produces(NuGetOutputDirectory / $"{AiunitReviewToolPackageId}.*.nupkg")
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(s =>
            {
                s = s
                    .SetProject(AiunitDesktopProjectPath)
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(NuGetOutputDirectory)
                    .SetNoBuild(true)
                    .SetNoRestore(true)
                    .SetProperty("ContinuousIntegrationBuild", IsServerBuild)
                    .SetVerbosity(DotNetVerbosity.minimal);

                s = s
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion);

                var pkgVer = !string.IsNullOrWhiteSpace(Version) ? Version : GitVersion.NuGetVersionV2;
                s = s.SetVersion(pkgVer);

                return s;
            });
        });

    Target PackLibrary => _ => _
        .DependsOn(Compile)
        .Produces(NuGetOutputDirectory / "SharpNinja.aiUnit.*.nupkg")
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(s =>
            {
                s = s
                    .SetProject(SourceDirectory / "SharpNinja.AiUnit" / "SharpNinja.AiUnit.csproj")
                    .SetConfiguration(Configuration)
                    .SetOutputDirectory(NuGetOutputDirectory)
                    .SetNoBuild(true)
                    .SetNoRestore(true)
                    .SetProperty("ContinuousIntegrationBuild", IsServerBuild)
                    .SetVerbosity(DotNetVerbosity.minimal);

                s = s
                    .SetAssemblyVersion(GitVersion.AssemblySemVer)
                    .SetFileVersion(GitVersion.AssemblySemFileVer)
                    .SetInformationalVersion(GitVersion.InformationalVersion);

                var pkgVer = !string.IsNullOrWhiteSpace(Version) ? Version : GitVersion.NuGetVersionV2;
                s = s.SetVersion(pkgVer);

                return s;
            });
        });

    Target Pack => _ => _
        .DependsOn(PackLibrary, PackAiunit, PackAiunitReview)
        .Description("Pack the library + both dotnet tools (matches original solution pack behavior).");

    // Redeploy targets - for local development (uninstall + install from local artifacts)
    Target RedeployAiunit => _ => _
        .DependsOn(PackAiunit)
        .Description("Pack the aiunit (REPL) tool and **forcefully redeploy it globally** (terminates running instances, " +
                     "deletes the versioned tool store, removes shims, uninstalls, then installs the freshly built version). " +
                     "This is the single command for local development - no more manual kill / delete / install steps.")
        .Executes(() =>
        {
            RedeployTool(
                packageId: AiunitToolPackageId,
                commandName: "aiunit",
                nupkgGlob: $"{AiunitToolPackageId}.*.nupkg");
        });

    Target RedeployAiunitReview => _ => _
        .DependsOn(PackAiunitReview)
        .Description("Pack the aiunit-review (Desktop) tool and **forcefully redeploy it globally** (terminates running instances, " +
                     "deletes the versioned tool store, removes shims, uninstalls, then installs the freshly built version). " +
                     "This is the single command for local development - no more manual kill / delete / install steps.")
        .Executes(() =>
        {
            RedeployTool(
                packageId: AiunitReviewToolPackageId,
                commandName: "aiunit-review",
                nupkgGlob: $"{AiunitReviewToolPackageId}.*.nupkg");
        });

    Target Redeploy => _ => _
        .DependsOn(RedeployAiunit, RedeployAiunitReview)
        .Description("Forcefully redeploy **both** dotnet tools globally (see individual Redeploy* targets for details).");

    Target Ci => _ => _
        .DependsOn(Test, Pack)
        .Description("CI target: test + pack both tools. Smoke tests and publishing are handled in azure-pipelines.yml or extended here.");

    // Smoke test targets (used by CI to validate packed tools without polluting global environment)
    Target SmokeTestAiunit => _ => _
        .DependsOn(PackAiunit)
        .Description("Temporarily install the aiunit tool from local packages and run --help / --version smoke tests.")
        .Executes(() =>
        {
            SmokeTestTool(
                packageId: AiunitToolPackageId,
                commandName: "aiunit",
                exeName: "aiunit.exe");
        });

    Target SmokeTestAiunitReview => _ => _
        .DependsOn(PackAiunitReview)
        .Description("Temporarily install the aiunit-review tool and run a non-UI probe smoke test.")
        .Executes(() =>
        {
            var toolPath = TemporaryDirectory / "tool-smoke-review";
            if (Directory.Exists(toolPath)) Directory.Delete(toolPath, recursive: true);
            Directory.CreateDirectory(toolPath);

            var nupkgs = NuGetOutputDirectory.GlobFiles($"{AiunitReviewToolPackageId}.*.nupkg").ToList();
            if (nupkgs.Count == 0)
                throw new Exception($"No {AiunitReviewToolPackageId} package found for smoke test.");

            var nupkg = nupkgs.OrderByDescending(x => x.Name).First();
            var match = System.Text.RegularExpressions.Regex.Match(nupkg.NameWithoutExtension, $@"^{System.Text.RegularExpressions.Regex.Escape(AiunitReviewToolPackageId)}\.(?<version>.+)$");
            if (!match.Success)
                throw new Exception($"Could not parse version from {nupkg.Name}");

            var version = match.Groups["version"].Value;

            DotNetTasks.DotNet($"tool install {AiunitReviewToolPackageId} --tool-path \"{toolPath}\" --add-source \"{NuGetOutputDirectory}\" --version {version}");

            // The Desktop tool supports --probe-exit for headless/CI validation (returns 0 without showing UI)
            Logger.Info("Running probe-exit smoke test for aiunit-review...");
            var probeExe = toolPath / "aiunit-review.exe";
            if (!File.Exists(probeExe))
                probeExe = toolPath / "aiunit-review";

            var result = ProcessTasks.StartProcess(probeExe, "--probe-exit", logInvocation: false, logOutput: true).AssertWaitForExit();
            if (result.ExitCode != 0)
                throw new Exception("aiunit-review --probe-exit smoke test failed.");
        });

    void SmokeTestTool(string packageId, string commandName, string exeName)
    {
        var toolPath = TemporaryDirectory / "tool-smoke";
        if (Directory.Exists(toolPath)) Directory.Delete(toolPath, recursive: true);
        Directory.CreateDirectory(toolPath);

        var nupkgs = NuGetOutputDirectory.GlobFiles($"{packageId}.*.nupkg").ToList();
        if (nupkgs.Count == 0)
            throw new Exception($"No {packageId} package found for smoke test.");

        var nupkg = nupkgs.OrderByDescending(x => x.Name).First();
        var match = System.Text.RegularExpressions.Regex.Match(nupkg.NameWithoutExtension, $@"^{System.Text.RegularExpressions.Regex.Escape(packageId)}\.(?<version>.+)$");
        if (!match.Success)
            throw new Exception($"Could not parse version from {nupkg.Name}");

        var version = match.Groups["version"].Value;

        DotNetTasks.DotNet($"tool install {packageId} --tool-path \"{toolPath}\" --add-source \"{NuGetOutputDirectory}\" --version {version}");

        var exe = toolPath / exeName;
        if (!File.Exists(exe))
            exe = toolPath / commandName; // fallback for some installs

        Logger.Info($"Running smoke tests for {commandName} v{version}...");

        // --help
        var helpResult = ProcessTasks.StartProcess(exe, "--help", logInvocation: false, logOutput: true).AssertWaitForExit();
        if (helpResult.ExitCode != 0)
            throw new Exception($"Installed {commandName} --help smoke test failed.");

        // --version
        var versionResult = ProcessTasks.StartProcess(exe, "--version", logInvocation: false, logOutput: true).AssertWaitForExit();
        if (versionResult.ExitCode != 0)
            throw new Exception($"Installed {commandName} --version smoke test failed.");
    }

    // Helper kept for potential future use / logging.
    // Version selection logic:
    // - If explicit --version parameter is passed (from CI on stable tags), use it for package Version/PackageVersion.
    // - Otherwise, do NOT force it — this lets GitVersion.MsBuild (injected by Directory.Build.props
    //   for all src projects) perform its normal "bumping" (calculating the version per GitVersion.yml,
    //   current branch, commits, etc.) so that GitVersion is fully honored on all artifacts (nupkgs,
    //   assemblies, symbols, etc.).
    string GetEffectiveVersion()
    {
        if (!string.IsNullOrWhiteSpace(Version))
            return Version;

        return GitVersion.NuGetVersionV2;
    }

    void RedeployTool(string packageId, string commandName, string nupkgGlob)
    {
        // Find the freshly packed nupkg in the local artifacts directory
        var nupkgs = NuGetOutputDirectory.GlobFiles(nupkgGlob).ToList();
        if (nupkgs.Count == 0)
            throw new Exception($"No package matching {nupkgGlob} found in {NuGetOutputDirectory}");

        var nupkg = nupkgs.OrderByDescending(f => f.Name).First();
        var fileName = Path.GetFileNameWithoutExtension(nupkg.Name);
        // Expected format: PackageId.Version
        var versionPart = fileName.Substring(packageId.Length + 1);

        Serilog.Log.Information($"Redeploying {commandName} from {nupkg} (version {versionPart})");

        if (IsServerBuild)
        {
            Serilog.Log.Warning("Running on build server - skipping destructive global tool cleanup (kill + store delete). " +
                                "Use the SmokeTest* targets for isolated verification instead.");
            // Still do a normal install attempt (may be useful in some self-hosted scenarios).
            DotNetTasks.DotNet($"tool install {packageId} --global --add-source \"{NuGetOutputDirectory}\" --version {versionPart}");
            return;
        }

        // === The following steps used to be manual (kill + delete .store + remove shims + uninstall + install).
        // They are now fully automated inside the Nuke Redeploy* targets so that
        //   ./build.ps1 --target RedeployAiunitReview
        // (or Redeploy) is the single command that leaves a working global installation of the freshly built bits.
        // This eliminates the repeated "access denied", "file in use", and "still running the old version" problems on Windows.

        // 1. Terminate any running instances of the tool (GUI or console) that would lock the store directory.
        //    This must happen before we touch the .store folder.
        ForceKillToolProcesses(commandName, packageId);

        // 2. Aggressively remove the exact versioned store directory that dotnet tool uses.
        //    On Windows the files inside are frequently locked by the previous process or the shim host.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var storeDir = (AbsolutePath)Path.Combine(userProfile, ".dotnet", "tools", ".store",
            packageId.ToLowerInvariant(), versionPart);

        if (Directory.Exists(storeDir))
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    storeDir.DeleteDirectory();
                    Serilog.Log.Information($"Deleted global tool store: {storeDir}");
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == 4)
                        Serilog.Log.Warning($"Could not fully delete {storeDir} after kills: {ex.Message}. Continuing with uninstall+install.");
                    else
                        Thread.Sleep(400);
                }
            }
        }

        // 3. Remove the shims in ~/.dotnet/tools (the things that end up on PATH).
        var toolsDir = (AbsolutePath)Path.Combine(userProfile, ".dotnet", "tools");
        var shimNames = new[] { commandName, commandName + ".exe", commandName + ".pdb" };
        foreach (var name in shimNames)
        {
            var shim = toolsDir / name;
            if (File.Exists(shim))
            {
                try { shim.DeleteFile(); } catch { /* best effort */ }
            }
        }

        // 4. Uninstall (best effort - the store delete + kill above usually makes this succeed).
        try
        {
            DotNetTasks.DotNet($"tool uninstall {packageId} --global");
        }
        catch
        {
            // Expected to sometimes fail if the tool wasn't registered or files were already removed.
        }

        // 5. Install the exact version we just built from the local artifacts feed.
        DotNetTasks.DotNet($"tool install {packageId} --global --add-source \"{NuGetOutputDirectory}\" --version {versionPart}");

        Serilog.Log.Information($"Global redeploy of {commandName} complete (version {versionPart}).");
        Serilog.Log.Information("Run the tool from any shell (it will use the newly installed version). " +
                    "Any previously running GUI instance was terminated so the new bits are picked up.");
    }

    /// <summary>
    /// Best-effort termination of any process that would keep the global tool store or shims locked.
    /// Called automatically by the Redeploy* targets.
    /// </summary>
    private static void ForceKillToolProcesses(string commandName, string packageId)
    {
        // Broad set of names that have been observed to hold locks on the store.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            commandName,
            commandName + ".exe",
            "aiunit",
            "aiunit.exe",
            "aiunit-review",
            "aiunit-review.exe",
            "SharpNinja.AiUnit.Desktop",
            "SharpNinja.AiUnit.Desktop.exe"
        };

        // Kill by process name (works on Windows and Unix)
        foreach (var name in candidates)
        {
            try
            {
                var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));
                foreach (var proc in procs)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1500);
                    }
                    catch { /* ignore individual process failures */ }
                }
            }
            catch { /* ignore */ }
        }

        // Extra Windows hammer via taskkill (covers processes that may not be found by name alone)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var patterns = new[] { "aiunit*", "*SharpNinja*Desktop*", "*aiunit-review*" };
            foreach (var pattern in patterns)
            {
                try
                {
                    // /T = tree, /F = force. Swallow all output.
                    ProcessTasks.StartProcess("taskkill", $"/F /IM {pattern} /T",
                            logInvocation: false, logOutput: false)
                        .AssertWaitForExit();
                }
                catch { /* best effort, may say "no tasks found" */ }
            }
        }

        // Give the OS a moment to release file handles.
        Thread.Sleep(600);
    }
}
