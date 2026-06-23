using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SharpNinja.AiUnit.Review;

namespace SharpNinja.AiUnit.Tests.Review;

public sealed class GrokBridgeTests
{
	private static readonly string ValidReviewJson =
		$$"""{"schemaVersion":"{{AiReviewFindingsSchema.SchemaVersion}}","reviewType":"code","status":"pass","summary":"fake grok pass","findings":[]}""";

	[Fact]
	public async Task Bridge_PreservesNonZeroGrokExit_WhenStdoutContainsValidReviewJson()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 7);

		try
		{
			var envCapturePath = Path.Combine(diagnostics, "grok-env.txt");
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_FAKE_GROK_ENV_PATH"] = envCapturePath;

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;
			var stderr = await stderrTask;

			Assert.Equal(7, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());
			Assert.Contains("preserving the exit code", stderr, StringComparison.OrdinalIgnoreCase);

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal(7, root.GetProperty("processExitCode").GetInt32());
			Assert.Equal(7, root.GetProperty("returnedExitCode").GetInt32());
			Assert.True(root.GetProperty("validReviewJson").GetBoolean());
			Assert.Equal("grok-build", root.GetProperty("aiUnitModel").GetString());
			Assert.Equal("grok-build", root.GetProperty("aiUnitModelVersion").GetString());
			Assert.Equal("grok-build", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-build", arguments);

			var envCapture = await File.ReadAllLinesAsync(envCapturePath);
			Assert.Contains("GROK_CURSOR_MCPS_ENABLED=0", envCapture);
			Assert.Contains("GROK_CLAUDE_MCPS_ENABLED=0", envCapture);
			Assert.Contains("GROK_MANAGED_MCPS_ENABLED=0", envCapture);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_UsesCallerWorkspaceAndGenericAiUnitModel()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		await File.WriteAllTextAsync(Path.Combine(workspace, "RiskyStars.sln"), string.Empty);
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace: null, diagnostics: null, fakeGrok);
			process.StartInfo.WorkingDirectory = workspace;
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-build";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;
			var stderr = await stderrTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());
			Assert.Contains("aiUnit Grok bridge diagnostics:", stderr, StringComparison.OrdinalIgnoreCase);

			var metadataPath = Assert.Single(Directory.GetFiles(
				Path.Combine(workspace, "artifacts", "aiunit-review", "grok-bridge"),
				"metadata.json",
				SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal(workspace, root.GetProperty("workspace").GetString());
			Assert.Equal("grok-build", root.GetProperty("aiUnitModel").GetString());
			Assert.Equal("grok-build", root.GetProperty("aiUnitModelVersion").GetString());
			Assert.Equal("grok-build", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--cwd", arguments);
			Assert.Contains(workspace, arguments);
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-build", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
		}
	}

	[Fact]
	public async Task Bridge_UsesConfiguredAiUnitGrokBuildModelAsCliModel()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-build";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal("grok-build", root.GetProperty("aiUnitModel").GetString());
			Assert.Equal("grok-build", root.GetProperty("aiUnitModelVersion").GetString());
			Assert.Equal("grok-build", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-build", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_UsesGenericAiUnitModelOverrideEvenWhenItIsGrok43()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "grok-4.3";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-4.3";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal("grok-4.3", root.GetProperty("aiUnitModel").GetString());
			Assert.Equal("grok-4.3", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-4.3", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_ReturnsTimeout_WhenGrokDoesNotExit()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateSlowFakeGrokAsync(workspace, "fake-grok-slow", ValidReviewJson);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_GROK_TIMEOUT_SECONDS"] = "1";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;
			var stderr = await stderrTask;

			Assert.Equal(124, process.ExitCode);
			Assert.Empty(stdout);
			Assert.Contains("did not exit within 1 seconds", stderr, StringComparison.OrdinalIgnoreCase);

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			Assert.True(metadata.RootElement.GetProperty("timedOut").GetBoolean());
			Assert.Equal(124, metadata.RootElement.GetProperty("returnedExitCode").GetInt32());
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_IgnoresLegacyAiUnitGrokModel_AndUsesAiUnitModel()
	{
		// WS-A regression guard: the bridge must resolve the model from the shared
		// AIUNIT_MODEL path like every other strategy. The retired AIUNIT_GROK_MODEL
		// variable must have no effect even when present in the environment.
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-build";
			process.StartInfo.Environment["AIUNIT_GROK_MODEL"] = "legacy-should-be-ignored";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal("grok-build", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-build", arguments);
			Assert.DoesNotContain("legacy-should-be-ignored", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_FallsBackToAiUnitModelVersion_WhenAiUnitModelIsPlaceholder()
	{
		// AIUNIT_MODEL holds a placeholder like "(cli-managed)"; the bridge falls back
		// to AIUNIT_MODEL_VERSION, matching the shared resolution contract.
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "(cli-managed)";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "grok-build";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal("grok-build", root.GetProperty("model").GetString());
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--model", arguments);
			Assert.Contains("grok-build", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_OmitsModelFlag_WhenBothModelVariablesArePlaceholders()
	{
		// Neither variable resolves to a concrete model (both are "(...)" placeholders),
		// so the bridge omits --model entirely and records a null model.
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_MODEL"] = "(cli-managed)";
			process.StartInfo.Environment["AIUNIT_MODEL_VERSION"] = "(cli-managed)";

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			var stdout = await stdoutTask;

			Assert.Equal(0, process.ExitCode);
			Assert.Equal(ValidReviewJson, stdout.Trim());

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var root = metadata.RootElement;
			Assert.Equal(JsonValueKind.Null, root.GetProperty("model").ValueKind);
			var arguments = root.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.DoesNotContain("--model", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_PassesDisallowedTools_WhenConfigured()
	{
		// WS-C: AIUNIT_GROK_DISALLOWED_TOOLS is forwarded as `--disallowed-tools <list>`
		// (a real Grok headless flag) so a harness can strip tools from a review.
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			const string denylist = "run_terminal_cmd,web_search,web_fetch";

			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment["AIUNIT_GROK_DISALLOWED_TOOLS"] = denylist;

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			await stdoutTask;

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var arguments = metadata.RootElement.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.Contains("--disallowed-tools", arguments);
			Assert.Contains(denylist, arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	[Fact]
	public async Task Bridge_OmitsDisallowedTools_WhenUnset()
	{
		var workspace = CreateTempDirectory("aiunit-grok-workspace-");
		var diagnostics = CreateTempDirectory("aiunit-grok-diagnostics-");
		var fakeGrok = await CreateFakeGrokAsync(workspace, "fake-grok", ValidReviewJson, exitCode: 0);

		try
		{
			using var process = CreateBridgeProcess(workspace, diagnostics, fakeGrok);
			process.StartInfo.ArgumentList.Add("Review type: code");
			process.StartInfo.Environment.Remove("AIUNIT_GROK_DISALLOWED_TOOLS");

			Assert.True(process.Start(), "Expected Grok bridge process to start.");
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
			var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
			await process.StandardError.ReadToEndAsync(timeout.Token);
			await process.WaitForExitAsync(timeout.Token);
			await stdoutTask;

			var metadataPath = Assert.Single(Directory.GetFiles(diagnostics, "metadata.json", SearchOption.AllDirectories));
			using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath));
			var arguments = metadata.RootElement.GetProperty("arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
			Assert.DoesNotContain("--disallowed-tools", arguments);
		}
		finally
		{
			TryDeleteDirectory(workspace);
			TryDeleteDirectory(diagnostics);
		}
	}

	private static Process CreateBridgeProcess(string? workspace, string? diagnostics, string fakeGrok)
	{
		var bridge = ResolveBridgeExecutable();
		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = bridge.FileName,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			}
		};

		foreach (var argument in bridge.Arguments)
		{
			process.StartInfo.ArgumentList.Add(argument);
		}

		process.StartInfo.Environment["AIUNIT_GROK_COMMAND"] = fakeGrok;
		if (!string.IsNullOrWhiteSpace(workspace))
		{
			process.StartInfo.Environment["AIUNIT_REVIEW_WORKSPACE"] = workspace;
		}
		if (!string.IsNullOrWhiteSpace(diagnostics))
		{
			process.StartInfo.Environment["AIUNIT_GROK_DIAGNOSTICS_DIR"] = diagnostics;
		}
		return process;
	}

	private static BridgeExecutable ResolveBridgeExecutable()
	{
		var baseDirectory = AppContext.BaseDirectory;
		var windowsExe = Path.Combine(baseDirectory, "SharpNinja.AiUnit.GrokBridge.exe");
		if (File.Exists(windowsExe))
		{
			return new BridgeExecutable(windowsExe, []);
		}

		var unixExe = Path.Combine(baseDirectory, "SharpNinja.AiUnit.GrokBridge");
		if (File.Exists(unixExe))
		{
			return new BridgeExecutable(unixExe, []);
		}

		var dll = Path.Combine(baseDirectory, "SharpNinja.AiUnit.GrokBridge.dll");
		Assert.True(File.Exists(dll), $"Expected Grok bridge executable or dll in {baseDirectory}.");
		return new BridgeExecutable("dotnet", [dll]);
	}

	private static async Task<string> CreateFakeGrokAsync(string workspace, string name, string stdout, int exitCode)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			var path = Path.Combine(workspace, name + ".cmd");
				await File.WriteAllTextAsync(
					path,
					string.Join(Environment.NewLine,
					[
						"@echo off",
						"if not \"%AIUNIT_FAKE_GROK_ENV_PATH%\"==\"\" (",
						"  (",
						"    echo GROK_CURSOR_MCPS_ENABLED=%GROK_CURSOR_MCPS_ENABLED%",
						"    echo GROK_CLAUDE_MCPS_ENABLED=%GROK_CLAUDE_MCPS_ENABLED%",
						"    echo GROK_MANAGED_MCPS_ENABLED=%GROK_MANAGED_MCPS_ENABLED%",
						"  ) > \"%AIUNIT_FAKE_GROK_ENV_PATH%\"",
						")",
						"echo " + stdout,
						$"exit /b {exitCode}"
					]),
				new UTF8Encoding(false));
			return path;
		}

		var shellPath = Path.Combine(workspace, name + ".sh");
			await File.WriteAllTextAsync(
				shellPath,
				$"#!/usr/bin/env sh\nif [ -n \"$AIUNIT_FAKE_GROK_ENV_PATH\" ]; then\n  printf '%s\\n' \"GROK_CURSOR_MCPS_ENABLED=$GROK_CURSOR_MCPS_ENABLED\" > \"$AIUNIT_FAKE_GROK_ENV_PATH\"\n  printf '%s\\n' \"GROK_CLAUDE_MCPS_ENABLED=$GROK_CLAUDE_MCPS_ENABLED\" >> \"$AIUNIT_FAKE_GROK_ENV_PATH\"\n  printf '%s\\n' \"GROK_MANAGED_MCPS_ENABLED=$GROK_MANAGED_MCPS_ENABLED\" >> \"$AIUNIT_FAKE_GROK_ENV_PATH\"\nfi\nprintf '%s\\n' '{stdout.Replace("'", "'\\''", StringComparison.Ordinal)}'\nexit {exitCode}\n",
				new UTF8Encoding(false));
		File.SetUnixFileMode(
			shellPath,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return shellPath;
	}

	private static async Task<string> CreateSlowFakeGrokAsync(string workspace, string name, string stdout)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			var path = Path.Combine(workspace, name + ".cmd");
			await File.WriteAllTextAsync(
				path,
				string.Join(Environment.NewLine,
				[
					"@echo off",
					"ping 127.0.0.1 -n 5 >nul",
					"echo " + stdout
				]),
				new UTF8Encoding(false));
			return path;
		}

		var shellPath = Path.Combine(workspace, name + ".sh");
		await File.WriteAllTextAsync(
			shellPath,
			$"#!/usr/bin/env sh\nsleep 5\nprintf '%s\\n' '{stdout.Replace("'", "'\\''", StringComparison.Ordinal)}'\n",
			new UTF8Encoding(false));
		File.SetUnixFileMode(
			shellPath,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return shellPath;
	}

	private static string CreateTempDirectory(string prefix)
	{
		var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
			// Best-effort cleanup for test temp directories.
		}
	}

	private sealed record BridgeExecutable(string FileName, string[] Arguments);
}
