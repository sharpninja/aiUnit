using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Cli;

/// <summary>
/// Frontier client that delegates to a local CLI binary (Claude Code, OpenAI
/// Codex, etc.) so the operator's existing subscription auth is reused and
/// no API key is needed.
///
/// Image attachments are written to a temp directory and referenced inline
/// in the prompt as <c>Image attachment (&lt;name&gt;): &lt;path&gt;</c>;
/// both the Claude and Codex CLIs surface local image paths through that
/// pattern. Text attachments are concatenated under fenced blocks ahead of
/// the user message.
///
/// Per-command flag dispatch:
///   claude   -&gt; <c>claude --print --output-format json --dangerously-skip-permissions [--model &lt;id&gt;]</c>, prompt via stdin
///   codex    -&gt; <c>codex exec --skip-git-repo-check [--model &lt;id&gt;] &lt;prompt&gt;</c>
///   (other)  -&gt; <c>&lt;command&gt; &lt;prompt&gt;</c>
/// </summary>
public sealed class CliFrontierClient : IFrontierModelClient
{
	private readonly string _command;
	private readonly string _providerName;
	private readonly string _modelVersion;
	private readonly TimeSpan _timeout;
	private readonly ILogger<CliFrontierClient> _logger;
	private readonly IProcessRunner _processRunner;

	/// <summary>
	/// Construct a CLI-backed frontier client. <paramref name="processRunner"/>
	/// defaults to <see cref="DefaultProcessRunner.Instance"/> when null.
	/// </summary>
	/// <param name="providerName">Short provider name used in telemetry (e.g. "claude").</param>
	/// <param name="command">CLI binary name or absolute path to invoke.</param>
	/// <param name="timeout">Per-call timeout enforced by the runner.</param>
	/// <param name="logger">Typed logger.</param>
	/// <param name="modelVersion">Optional model id; "(...)" wrapped values are treated as placeholders and skipped.</param>
	/// <param name="processRunner">Optional process-runner seam for testing.</param>
	public CliFrontierClient(
		string providerName,
		string command,
		TimeSpan timeout,
		ILogger<CliFrontierClient> logger,
		string? modelVersion = null,
		IProcessRunner? processRunner = null)
	{
		_providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
		_command = command ?? throw new ArgumentNullException(nameof(command));
		_timeout = timeout;
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_modelVersion = string.IsNullOrWhiteSpace(modelVersion) ? "(cli-managed)" : modelVersion!;
		_processRunner = processRunner ?? DefaultProcessRunner.Instance;
	}

	/// <inheritdoc />
	public string Provider => _providerName;

	/// <inheritdoc />
	public string ModelVersion => _modelVersion;

	/// <inheritdoc />
	public async Task<FrontierResponse> SendAsync(FrontierRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var sw = Stopwatch.StartNew();

		// Workspace dir for attachments. Cleaned up at the end so successive
		// scenarios do not leak PNGs. Failures still clean via finally.
		var workspace = Path.Combine(Path.GetTempPath(), "aiunit-cli-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspace);
		try
		{
			var prompt = BuildPrompt(request, workspace);
			var (psi, stdinPrompt) = BuildProcessStartInfo(prompt);

			ProcessExecutionResult result;
			try
			{
				result = await _processRunner
					.RunAsync(psi, stdinPrompt, _timeout, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				return Fail(sw, "spawn_failed",
					$"Could not start '{_command}': {ex.Message}", null);
			}

			if (result.TimedOut)
			{
				return Fail(sw, "timeout",
					$"'{_command}' did not exit within {_timeout.TotalSeconds:F0}s.", null);
			}
			if (result.ExitCode != 0)
			{
				var excerpt = (string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr).Trim();
				if (excerpt.Length > 500) excerpt = excerpt.Substring(0, 500) + "...";
				return Fail(sw, "cli_error",
					$"'{_command}' exited with code {result.ExitCode}: {excerpt}", result.ExitCode);
			}

			var text = ExtractAssistantText(result.Stdout, _command);
			if (string.IsNullOrWhiteSpace(text))
			{
				return Fail(sw, "empty_response",
					$"'{_command}' produced no usable output. stderr excerpt: " +
					(result.Stderr.Length > 200 ? result.Stderr.Substring(0, 200) : result.Stderr), null);
			}

			_logger.LogInformation(
				"CLI {Provider} ok: command='{Command}' latencyMs={Latency} stdoutLen={Len}",
				_providerName, _command, sw.ElapsedMilliseconds, result.Stdout.Length);

			return new FrontierResponse(
				Text: text,
				TokenUsage: FrontierTokenUsage.Zero,
				LatencyMs: sw.ElapsedMilliseconds,
				Provider: _providerName,
				ModelVersion: ModelVersion,
				EstimatedCostUsd: null,
				Error: null);
		}
		finally
		{
			try
			{
				if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
			}
			catch
			{
				// non-fatal, leave temp dir for OS cleanup
			}
		}
	}

	private static string BuildPrompt(FrontierRequest request, string workspaceDir)
	{
		var sb = new StringBuilder();
		sb.Append(request.SystemPrompt ?? string.Empty);
		sb.Append("\n\n");
		sb.Append(request.UserMessage ?? string.Empty);

		var attachments = request.Attachments ?? (IReadOnlyList<FrontierAttachment>)Array.Empty<FrontierAttachment>();
		foreach (var att in attachments)
		{
			if (att is null) continue;
			if (att.IsImage)
			{
				// Write image to workspace + reference by absolute path. Both
				// Claude CLI and Codex CLI accept local file refs inline.
				var safeName = string.Concat(att.Name.Split(Path.GetInvalidFileNameChars()));
				if (string.IsNullOrEmpty(safeName)) safeName = "attachment.png";
				var path = Path.Combine(workspaceDir, safeName);
				File.WriteAllBytes(path, att.Data ?? Array.Empty<byte>());
				sb.Append("\n\nImage attachment (").Append(att.Name).Append("): ").Append(path);
			}
			else
			{
				// Inline text attachments under a fenced section so the model
				// reads them as referenced material, not free-text prose.
				sb.Append("\n\n--- ").Append(att.Name).Append(" ---\n");
				try { sb.Append(Encoding.UTF8.GetString(att.Data ?? Array.Empty<byte>())); }
				catch { sb.Append("<binary attachment>"); }
				sb.Append("\n--- end ").Append(att.Name).Append(" ---\n");
			}
		}

		return sb.ToString();
	}

	/// <summary>
	/// Build the <see cref="ProcessStartInfo"/> for the named CLI. Returns
	/// (psi, stdinPrompt) - when stdinPrompt is non-null the caller writes
	/// it to <c>StandardInput</c>; otherwise the prompt is already encoded
	/// in <c>ArgumentList</c>.
	/// </summary>
	private (ProcessStartInfo Psi, string? StdinPrompt) BuildProcessStartInfo(string prompt)
	{
		var cmdLower = _command.Trim().ToLowerInvariant();
		var psi = new ProcessStartInfo
		{
			FileName = _command,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
		};

		switch (cmdLower)
		{
			case "claude":
				psi.ArgumentList.Add("--print");
				psi.ArgumentList.Add("--output-format");
				psi.ArgumentList.Add("json");
				psi.ArgumentList.Add("--dangerously-skip-permissions");
				if (IsConcreteModel(_modelVersion))
				{
					psi.ArgumentList.Add("--model");
					psi.ArgumentList.Add(_modelVersion);
				}
				psi.RedirectStandardInput = true;
				return (psi, prompt);

			case "codex":
				psi.ArgumentList.Add("exec");
				psi.ArgumentList.Add("--skip-git-repo-check");
				if (IsConcreteModel(_modelVersion))
				{
					psi.ArgumentList.Add("--model");
					psi.ArgumentList.Add(_modelVersion);
				}
				psi.ArgumentList.Add(prompt);
				return (psi, null);

			default:
				// Unknown CLI: pass the prompt as a single positional arg.
				// Operators can override by setting AIUNIT_KIND=cli + AIUNIT_COMMAND=<their-cli>;
				// if their CLI needs different flags they should add a case here.
				psi.ArgumentList.Add(prompt);
				return (psi, null);
		}
	}

	/// <summary>
	/// Parses the CLI stdout. Claude's <c>--output-format json</c> shape is
	/// <c>{"type":"result","subtype":"success","result":"&lt;text&gt;","is_error":false}</c>;
	/// we extract the <c>result</c> field. Codex emits free text; we return
	/// stdout verbatim (trimmed).
	/// </summary>
	private static string ExtractAssistantText(string stdout, string command)
	{
		var trimmed = stdout.Trim();
		if (string.IsNullOrEmpty(trimmed)) return string.Empty;

		var cmdLower = command.Trim().ToLowerInvariant();
		if (cmdLower == "claude")
		{
			try
			{
				using var doc = JsonDocument.Parse(trimmed);
				if (doc.RootElement.ValueKind == JsonValueKind.Object
					&& doc.RootElement.TryGetProperty("result", out var resultEl)
					&& resultEl.ValueKind == JsonValueKind.String)
				{
					return resultEl.GetString() ?? string.Empty;
				}
			}
			catch (JsonException)
			{
				// Fall through; non-JSON Claude output (rare) returned raw.
			}
		}

		return trimmed;
	}

	private static bool IsConcreteModel(string? value) =>
		!string.IsNullOrWhiteSpace(value)
		&& !value.StartsWith("(", StringComparison.Ordinal);  // skip "(cli-managed)" placeholder

	private FrontierResponse Fail(Stopwatch sw, string errorCode, string message, int? httpStatus)
	{
		_logger.LogWarning(
			"CLI {Provider} failed: command='{Command}' code={Code} latencyMs={Latency} msg={Message}",
			_providerName, _command, errorCode, sw.ElapsedMilliseconds, message);
		return new FrontierResponse(
			Text: null,
			TokenUsage: FrontierTokenUsage.Zero,
			LatencyMs: sw.ElapsedMilliseconds,
			Provider: _providerName,
			ModelVersion: ModelVersion,
			EstimatedCostUsd: null,
			Error: new FrontierError(errorCode, message, httpStatus));
	}
}
