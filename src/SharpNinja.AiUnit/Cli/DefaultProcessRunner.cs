using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpNinja.AiUnit.Cli;

/// <summary>
/// Production <see cref="IProcessRunner"/>: spawns the configured process,
/// streams stdout + stderr, honors timeout via a linked
/// <see cref="CancellationTokenSource"/>, and reports a clean
/// <see cref="ProcessExecutionResult.TimedOut"/> flag on kill.
/// </summary>
public sealed class DefaultProcessRunner : IProcessRunner
{
	/// <summary>Process-wide singleton instance used as the default runner.</summary>
	public static readonly DefaultProcessRunner Instance = new();

	/// <inheritdoc />
	public async Task<ProcessExecutionResult> RunAsync(
		ProcessStartInfo startInfo,
		string? stdinPrompt,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(startInfo);

		// Ensure stdout/stderr are captured; the test seam never sees these
		// flags but the production path requires them.
		startInfo.RedirectStandardOutput = true;
		startInfo.RedirectStandardError = true;
		startInfo.UseShellExecute = false;
		startInfo.CreateNoWindow = true;
		startInfo.StandardOutputEncoding = Encoding.UTF8;
		startInfo.StandardErrorEncoding = Encoding.UTF8;
		if (stdinPrompt is not null)
		{
			startInfo.RedirectStandardInput = true;
		}

		using var process = new Process { StartInfo = startInfo };
		var stdoutSb = new StringBuilder();
		var stderrSb = new StringBuilder();
		process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutSb.AppendLine(e.Data); };
		process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderrSb.AppendLine(e.Data); };

		if (!process.Start())
		{
			return new ProcessExecutionResult(
				ExitCode: -1,
				Stdout: string.Empty,
				Stderr: $"Could not start '{startInfo.FileName}'.",
				TimedOut: false);
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		if (stdinPrompt is not null)
		{
			await process.StandardInput.WriteAsync(stdinPrompt.AsMemory(), cancellationToken).ConfigureAwait(false);
			process.StandardInput.Close();
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);
		var timedOut = false;
		try
		{
			await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
			timedOut = true;
		}
		catch (OperationCanceledException)
		{
			try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
			throw;
		}

		return new ProcessExecutionResult(
			ExitCode: timedOut ? -1 : process.ExitCode,
			Stdout: stdoutSb.ToString(),
			Stderr: stderrSb.ToString(),
			TimedOut: timedOut);
	}
}
