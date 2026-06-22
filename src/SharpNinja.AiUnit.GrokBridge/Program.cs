using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SharpNinja.AiUnit.Review;

namespace SharpNinja.AiUnit.GrokBridge;

internal static class Program
{
	private const string DefaultMaxTurns = "64";
	private const int DefaultTimeoutSeconds = 1800;

	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
		{
			await Console.Error.WriteLineAsync("aiUnit Grok bridge expected the review prompt as the first argument.");
			return 64;
		}

		var prompt = args.Length == 1 ? args[0] : string.Join(" ", args);
		var workspace = ResolveWorkspace();
		var diagnostics = CreateDiagnostics(workspace);
		var grokCommand = Environment.GetEnvironmentVariable("AIUNIT_GROK_COMMAND");
		if (string.IsNullOrWhiteSpace(grokCommand))
		{
			grokCommand = "grok";
		}

		try
		{
			await File.WriteAllTextAsync(diagnostics.PromptPath, prompt, new UTF8Encoding(false));
			var startedUtc = DateTimeOffset.UtcNow;
			var startInfo = BuildStartInfo(grokCommand, workspace, prompt);

			using var process = new Process
			{
				StartInfo = startInfo
			};

			if (!process.Start())
			{
				await WriteMetadataAsync(
					diagnostics,
					workspace,
					grokCommand,
					startInfo.ArgumentList.ToArray(),
					startedUtc,
					DateTimeOffset.UtcNow,
					processExitCode: null,
					returnedExitCode: 127,
					stdoutLength: 0,
					stderrLength: 0,
					forwardedStdoutLength: 0,
					extractedJson: false,
					validReviewJson: false,
					timedOut: false);
				await Console.Error.WriteLineAsync($"Could not start '{grokCommand}'.");
				await Console.Error.WriteLineAsync($"aiUnit Grok bridge diagnostics: {diagnostics.RunDirectory}");
				return 127;
			}

			var stdout = process.StandardOutput.ReadToEndAsync();
			var stderr = process.StandardError.ReadToEndAsync();
			var timedOut = false;
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ReadIntEnvironment(
				"AIUNIT_GROK_TIMEOUT_SECONDS",
				DefaultTimeoutSeconds)));
			try
			{
				await process.WaitForExitAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
				timedOut = true;
				TryKillProcessTree(process);
			}

			var stdoutText = await stdout;
			var stderrText = await stderr;
			if (timedOut)
			{
				await File.WriteAllTextAsync(diagnostics.StdoutPath, stdoutText, new UTF8Encoding(false));
				await File.WriteAllTextAsync(diagnostics.StderrPath, stderrText, new UTF8Encoding(false));
				await WriteMetadataAsync(
					diagnostics,
					workspace,
					grokCommand,
					startInfo.ArgumentList.ToArray(),
					startedUtc,
					DateTimeOffset.UtcNow,
					processExitCode: null,
					returnedExitCode: 124,
					stdoutText.Length,
					stderrText.Length,
					forwardedStdoutLength: 0,
					extractedJson: false,
					validReviewJson: false,
					timedOut: true);
				await Console.Error.WriteLineAsync(
					$"Grok did not exit within {ReadIntEnvironment("AIUNIT_GROK_TIMEOUT_SECONDS", DefaultTimeoutSeconds)} seconds.");
				await Console.Error.WriteLineAsync($"aiUnit Grok bridge diagnostics: {diagnostics.RunDirectory}");
				return 124;
			}

			var forwardedStdout = NormalizeStdout(stdoutText, out var extractedJson, out var validReviewJson);
			var returnedExitCode = process.ExitCode;

			await File.WriteAllTextAsync(diagnostics.StdoutPath, stdoutText, new UTF8Encoding(false));
			await File.WriteAllTextAsync(diagnostics.StderrPath, stderrText, new UTF8Encoding(false));
			if (extractedJson is not null)
			{
				await File.WriteAllTextAsync(diagnostics.ExtractedJsonPath, extractedJson, new UTF8Encoding(false));
			}
			await WriteMetadataAsync(
				diagnostics,
				workspace,
				grokCommand,
				startInfo.ArgumentList.ToArray(),
				startedUtc,
				DateTimeOffset.UtcNow,
				process.ExitCode,
				returnedExitCode,
				stdoutText.Length,
				stderrText.Length,
				forwardedStdout.Length,
				extractedJson is not null,
				validReviewJson,
				timedOut: false);

			Console.Out.Write(forwardedStdout);
			Console.Error.Write(stderrText);
			await Console.Error.WriteLineAsync($"aiUnit Grok bridge diagnostics: {diagnostics.RunDirectory}");
			if (process.ExitCode != 0 && validReviewJson)
			{
				await Console.Error.WriteLineAsync(
					$"Grok exited with code {process.ExitCode}; preserving the exit code even though stdout contained valid aiUnit review JSON.");
			}
			else if (process.ExitCode == 0 && !validReviewJson)
			{
				await Console.Error.WriteLineAsync(
					"Grok exited with code 0, but stdout did not contain valid aiUnit review findings JSON.");
			}

			return returnedExitCode;
		}
		catch (Exception ex)
		{
			await Console.Error.WriteLineAsync($"aiUnit Grok bridge failed: {ex.Message}");
			await Console.Error.WriteLineAsync($"aiUnit Grok bridge diagnostics: {diagnostics.RunDirectory}");
			return 1;
		}
	}

	private static ProcessStartInfo BuildStartInfo(string grokCommand, string workspace, string prompt)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = grokCommand,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};

		startInfo.Environment["NO_COLOR"] = "1";
		startInfo.Environment["CLICOLOR"] = "0";
		startInfo.Environment["TERM"] = "dumb";
		ConfigureHeadlessEnvironment(startInfo);

		startInfo.ArgumentList.Add("--cwd");
		startInfo.ArgumentList.Add(workspace);
		var model = ResolveExplicitGrokCliModelOverride();
		if (!string.IsNullOrWhiteSpace(model))
		{
			startInfo.ArgumentList.Add("--model");
			startInfo.ArgumentList.Add(model);
		}
		startInfo.ArgumentList.Add("--single");
		startInfo.ArgumentList.Add(prompt);
		startInfo.ArgumentList.Add("--output-format");
		startInfo.ArgumentList.Add("plain");

		var permissionMode = Environment.GetEnvironmentVariable("AIUNIT_GROK_PERMISSION_MODE");
		if (!string.IsNullOrWhiteSpace(permissionMode)
			&& !string.Equals(permissionMode, "none", StringComparison.OrdinalIgnoreCase))
		{
			startInfo.ArgumentList.Add("--permission-mode");
			startInfo.ArgumentList.Add(permissionMode);
		}

		if (ReadBooleanEnvironment("AIUNIT_GROK_DISABLE_WEB_SEARCH", defaultValue: true))
		{
			startInfo.ArgumentList.Add("--disable-web-search");
		}
		if (ReadBooleanEnvironment("AIUNIT_GROK_NO_SUBAGENTS", defaultValue: true))
		{
			startInfo.ArgumentList.Add("--no-subagents");
		}
		if (ReadBooleanEnvironment("AIUNIT_GROK_NO_ALT_SCREEN", defaultValue: true))
		{
			startInfo.ArgumentList.Add("--no-alt-screen");
		}

		startInfo.ArgumentList.Add("--max-turns");
		startInfo.ArgumentList.Add(Environment.GetEnvironmentVariable("AIUNIT_GROK_MAX_TURNS") ?? DefaultMaxTurns);

		return startInfo;
	}

	private static void ConfigureHeadlessEnvironment(ProcessStartInfo startInfo)
	{
		if (ReadBooleanEnvironment("AIUNIT_GROK_ENABLE_MCPS", defaultValue: false))
		{
			return;
		}

		startInfo.Environment["GROK_CURSOR_MCPS_ENABLED"] = "0";
		startInfo.Environment["GROK_CLAUDE_MCPS_ENABLED"] = "0";
		startInfo.Environment["GROK_MANAGED_MCPS_ENABLED"] = "0";
	}

	private static void TryKillProcessTree(Process process)
	{
		try
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
			// Best-effort timeout cleanup. The bridge still returns a timeout error.
		}
	}

	private static string? ResolveExplicitGrokCliModelOverride()
	{
		// AIUNIT_MODEL is logical aiUnit strategy metadata. Only the
		// provider-specific AIUNIT_GROK_MODEL variable may become grok --model.
		var value = Environment.GetEnvironmentVariable("AIUNIT_GROK_MODEL")?.Trim();
		return IsConcreteModel(value) ? value : null;
	}

	private static bool IsConcreteModel(string? value) =>
		value is not null
		&& !string.IsNullOrWhiteSpace(value)
		&& !value.StartsWith("(", StringComparison.Ordinal);

	private static BridgeDiagnostics CreateDiagnostics(string workspace)
	{
		var root = ResolveDiagnosticsRoot(workspace);
		var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff'Z'", CultureInfo.InvariantCulture);
		var discriminator = Guid.NewGuid().ToString("N")[..8];
		var runDirectory = Path.Combine(root, $"{timestamp}-{discriminator}");
		Directory.CreateDirectory(runDirectory);

		return new BridgeDiagnostics(
			runDirectory,
			Path.Combine(runDirectory, "prompt.md"),
			Path.Combine(runDirectory, "stdout.txt"),
			Path.Combine(runDirectory, "stderr.txt"),
			Path.Combine(runDirectory, "extracted-findings.json"),
			Path.Combine(runDirectory, "metadata.json"));
	}

	private static string ResolveDiagnosticsRoot(string workspace)
	{
		var fromEnv = Environment.GetEnvironmentVariable("AIUNIT_GROK_DIAGNOSTICS_DIR");
		if (string.IsNullOrWhiteSpace(fromEnv))
		{
			return Path.Combine(workspace, "artifacts", "aiunit-review", "grok-bridge");
		}

		var expanded = Environment.ExpandEnvironmentVariables(fromEnv);
		return Path.IsPathRooted(expanded) ? expanded : Path.Combine(workspace, expanded);
	}

	private static async Task WriteMetadataAsync(
		BridgeDiagnostics diagnostics,
		string workspace,
		string command,
		IReadOnlyList<string> arguments,
		DateTimeOffset startedUtc,
		DateTimeOffset endedUtc,
		int? processExitCode,
		int returnedExitCode,
		int stdoutLength,
		int stderrLength,
		int forwardedStdoutLength,
		bool extractedJson,
		bool validReviewJson,
		bool timedOut)
	{
		var metadata = new
		{
			workspace,
			command,
			arguments,
			aiUnitModel = Environment.GetEnvironmentVariable("AIUNIT_MODEL"),
			aiUnitModelVersion = Environment.GetEnvironmentVariable("AIUNIT_MODEL_VERSION"),
			grokCliModel = ResolveExplicitGrokCliModelOverride(),
			startedUtc = startedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			endedUtc = endedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
			processExitCode,
			returnedExitCode,
			stdoutLength,
			stderrLength,
			forwardedStdoutLength,
			extractedJson,
			validReviewJson,
			timedOut,
			diagnostics = new
			{
				diagnostics.PromptPath,
				diagnostics.StdoutPath,
				diagnostics.StderrPath,
				diagnostics.ExtractedJsonPath
			}
		};

		await File.WriteAllTextAsync(
			diagnostics.MetadataPath,
			JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
			new UTF8Encoding(false));
	}

	private static string NormalizeStdout(string stdout, out string? extractedJson, out bool validReviewJson)
	{
		extractedJson = null;
		validReviewJson = false;
		var trimmed = stdout.Trim();
		if (IsReviewFindingsJson(trimmed))
		{
			validReviewJson = true;
			return trimmed;
		}

		extractedJson = ExtractFirstReviewFindingsJson(stdout);
		if (extractedJson is not null)
		{
			validReviewJson = true;
			return extractedJson;
		}

		return stdout;
	}

	private static string? ExtractFirstReviewFindingsJson(string text)
	{
		var start = text.IndexOf('{', StringComparison.Ordinal);
		while (start >= 0)
		{
			var depth = 0;
			var inString = false;
			var escaped = false;

			for (var i = start; i < text.Length; i++)
			{
				var current = text[i];
				if (inString)
				{
					if (escaped)
					{
						escaped = false;
					}
					else if (current == '\\')
					{
						escaped = true;
					}
					else if (current == '"')
					{
						inString = false;
					}
					continue;
				}

				if (current == '"')
				{
					inString = true;
					continue;
				}
				if (current == '{')
				{
					depth++;
					continue;
				}
				if (current != '}')
				{
					continue;
				}

				depth--;
				if (depth == 0)
				{
					var candidate = text[start..(i + 1)].Trim();
					if (IsReviewFindingsJson(candidate))
					{
						return candidate;
					}
					break;
				}
			}

			start = text.IndexOf('{', start + 1);
		}

		return null;
	}

	private static bool IsReviewFindingsJson(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			var root = document.RootElement;
			return root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty("schemaVersion", out var schemaVersion)
				&& schemaVersion.ValueKind == JsonValueKind.String
				&& string.Equals(schemaVersion.GetString(), AiReviewFindingsSchema.SchemaVersion, StringComparison.Ordinal)
				&& root.TryGetProperty("reviewType", out var reviewType)
				&& reviewType.ValueKind == JsonValueKind.String
				&& root.TryGetProperty("status", out var status)
				&& status.ValueKind == JsonValueKind.String
				&& root.TryGetProperty("summary", out var summary)
				&& summary.ValueKind == JsonValueKind.String
				&& root.TryGetProperty("findings", out var findings)
				&& findings.ValueKind == JsonValueKind.Array;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool ReadBooleanEnvironment(string name, bool defaultValue)
	{
		var value = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(value))
		{
			return defaultValue;
		}

		return value.Trim().ToLowerInvariant() switch
		{
			"1" or "true" or "yes" or "on" => true,
			"0" or "false" or "no" or "off" => false,
			_ => defaultValue
		};
	}

	private static int ReadIntEnvironment(string name, int defaultValue)
	{
		var value = Environment.GetEnvironmentVariable(name);
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
			? parsed
			: defaultValue;
	}

	private static string ResolveWorkspace()
	{
		var fromEnv = Environment.GetEnvironmentVariable("AIUNIT_REVIEW_WORKSPACE");
		if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
		{
			return fromEnv;
		}

		var currentDirectory = Directory.GetCurrentDirectory();
		return FindWorkspaceRoot(currentDirectory)
			?? FindWorkspaceRoot(AppContext.BaseDirectory)
			?? currentDirectory;
	}

	private static string? FindWorkspaceRoot(string start)
	{
		var directory = new DirectoryInfo(start);
		while (directory is not null)
		{
			if (Directory.EnumerateFiles(directory.FullName, "*.sln").Any()
				|| Directory.Exists(Path.Combine(directory.FullName, ".git"))
				|| File.Exists(Path.Combine(directory.FullName, ".git")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		return null;
	}

	private sealed record BridgeDiagnostics(
		string RunDirectory,
		string PromptPath,
		string StdoutPath,
		string StderrPath,
		string ExtractedJsonPath,
		string MetadataPath);
}
