using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SharpNinja.AiUnit.Cli;

/// <summary>
/// Test-hermetic seam over <see cref="Process"/>.
/// <see cref="CliFrontierClient"/> depends on this interface so unit tests
/// can substitute a stub instead of spawning real CLI binaries. The default
/// production impl is <see cref="DefaultProcessRunner"/>.
/// </summary>
public interface IProcessRunner
{
	/// <summary>
	/// Run the configured process. When <paramref name="stdinPrompt"/> is
	/// non-null the runner is responsible for writing it to the process's
	/// standard input. Honors <paramref name="timeout"/> by setting
	/// <see cref="ProcessExecutionResult.TimedOut"/> = true when exceeded.
	/// </summary>
	/// <param name="startInfo">Configured <see cref="ProcessStartInfo"/> to launch.</param>
	/// <param name="stdinPrompt">Optional text to write to standard input.</param>
	/// <param name="timeout">Maximum time the process may run before kill.</param>
	/// <param name="cancellationToken">Caller-supplied cancellation token.</param>
	Task<ProcessExecutionResult> RunAsync(
		ProcessStartInfo startInfo,
		string? stdinPrompt,
		TimeSpan timeout,
		CancellationToken cancellationToken);
}

/// <summary>
/// Result of a single process invocation. <see cref="ExitCode"/> reflects
/// what the OS returned (or -1 on timeout kill). <see cref="TimedOut"/>
/// distinguishes a clean non-zero exit from a runner-enforced kill.
/// </summary>
/// <param name="ExitCode">Process exit code (or -1 on timeout / spawn failure).</param>
/// <param name="Stdout">Captured standard-output text.</param>
/// <param name="Stderr">Captured standard-error text.</param>
/// <param name="TimedOut">True when the runner enforced the timeout via kill.</param>
public sealed record ProcessExecutionResult(
	int ExitCode,
	string Stdout,
	string Stderr,
	bool TimedOut);
