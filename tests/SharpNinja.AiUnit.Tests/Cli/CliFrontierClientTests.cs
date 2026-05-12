using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpNinja.AiUnit.Cli;
using SharpNinja.AiUnit.Frontier;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Cli;

/// <summary>
/// Byrd Phase 1 tests for <see cref="CliFrontierClient"/>. Each fact targets
/// one acceptance line of the CLI strategy requirement set:
///
///   FR (CLI adapter): CLI strategies reuse operator subscription auth - no
///   API key required; the client must spawn the named CLI binary, pass the
///   prompt + attachments correctly, surface stdout as the model response,
///   and translate process failures (non-zero exit, timeout, empty stdout)
///   into <see cref="FrontierError"/> entries.
///
///   TR (test isolation): production process-spawn is replaced by an
///   <see cref="IProcessRunner"/> seam so the harness is hermetic and does
///   not require a real claude/codex binary on PATH.
/// </summary>
public sealed class CliFrontierClientTests
{
	private static FrontierRequest BuildRequest(
		string system = "system",
		string user = "user",
		IReadOnlyList<FrontierAttachment>? attachments = null) =>
		new(
			SystemPrompt: system,
			UserMessage: user,
			Attachments: attachments,
			MaxTokens: null,
			Temperature: 0.0,
			RequireJsonOutput: false);

	private static CliFrontierClient BuildClient(
		IProcessRunner runner,
		string command = "claude",
		TimeSpan? timeout = null,
		string? model = null) =>
		new(
			providerName: $"test:{command}",
			command: command,
			timeout: timeout ?? TimeSpan.FromSeconds(30),
			logger: NullLogger<CliFrontierClient>.Instance,
			modelVersion: model,
			processRunner: runner);

	[Fact]
	public async Task SendAsync_ClaudeJsonOutput_ExtractsResultField()
	{
		// Claude CLI emits {"type":"result","result":"<text>"} - the client
		// must parse + return the "result" string in FrontierResponse.Text.
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 0,
				Stdout: "{\"type\":\"result\",\"result\":\"hello world\"}",
				Stderr: string.Empty,
				TimedOut: false));

		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest());

		Assert.Null(response.Error);
		Assert.Equal("hello world", response.Text);
	}

	[Fact]
	public async Task SendAsync_CodexRawStdout_ReturnsVerbatim()
	{
		// Codex CLI emits free text - the client must not try to parse it as
		// JSON. Returned text is stdout trimmed.
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 0,
				Stdout: "  plain codex answer text\n",
				Stderr: string.Empty,
				TimedOut: false));

		var client = BuildClient(runner, command: "codex");
		var response = await client.SendAsync(BuildRequest());

		Assert.Null(response.Error);
		Assert.Equal("plain codex answer text", response.Text);
	}

	[Fact]
	public async Task SendAsync_ClaudeNonJsonStdout_FallsBackToRawText()
	{
		// Claude rare-path: when --output-format json mis-renders, raw text
		// must come through unchanged rather than being dropped on the floor.
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 0,
				Stdout: "not-json answer\n",
				Stderr: string.Empty,
				TimedOut: false));

		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest());

		Assert.Null(response.Error);
		Assert.Equal("not-json answer", response.Text);
	}

	[Fact]
	public async Task SendAsync_NonZeroExit_ReturnsCliError_WithStderrExcerpt()
	{
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 2,
				Stdout: string.Empty,
				Stderr: "auth failed: missing subscription",
				TimedOut: false));

		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest());

		Assert.Null(response.Text);
		Assert.NotNull(response.Error);
		Assert.Equal("cli_error", response.Error!.ErrorCode);
		Assert.Equal(2, response.Error.HttpStatus);
		Assert.Contains("auth failed", response.Error.Message);
	}

	[Fact]
	public async Task SendAsync_TimedOut_ReturnsTimeoutError()
	{
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: -1,
				Stdout: string.Empty,
				Stderr: string.Empty,
				TimedOut: true));

		var client = BuildClient(runner, command: "claude", timeout: TimeSpan.FromMilliseconds(50));
		var response = await client.SendAsync(BuildRequest());

		Assert.NotNull(response.Error);
		Assert.Equal("timeout", response.Error!.ErrorCode);
	}

	[Fact]
	public async Task SendAsync_EmptyStdout_ReturnsEmptyResponseError()
	{
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 0,
				Stdout: "   \n  ",
				Stderr: "noisy warning",
				TimedOut: false));

		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest());

		Assert.NotNull(response.Error);
		Assert.Equal("empty_response", response.Error!.ErrorCode);
	}

	[Fact]
	public async Task SendAsync_ImageAttachment_WritesTempFile_ReferencedInPrompt()
	{
		// The runner captures the prompt the client built. The prompt MUST
		// contain a file-path reference to the temp PNG, and that file must
		// have existed at call time (verified by reading + matching bytes).
		string? capturedPrompt = null;
		string? capturedImageContent = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPrompt = ci.ArgAt<string?>(1);
				// Pull the image path out of the prompt body and verify the
				// temp file exists at this moment - the client must not have
				// cleaned up the workspace yet.
				if (capturedPrompt is not null)
				{
					var marker = "Image attachment (shot.png): ";
					var idx = capturedPrompt.IndexOf(marker, StringComparison.Ordinal);
					if (idx >= 0)
					{
						var path = capturedPrompt[(idx + marker.Length)..]
							.Split('\n', '\r')[0].Trim();
						if (File.Exists(path))
						{
							capturedImageContent = Encoding.UTF8.GetString(File.ReadAllBytes(path));
						}
					}
				}
				return new ProcessExecutionResult(0, "{\"result\":\"ok\"}", "", false);
			});

		var imageBytes = Encoding.UTF8.GetBytes("fake-png-bytes");
		var attachment = new FrontierAttachment("image/png", "shot.png", imageBytes);
		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest(attachments: new[] { attachment }));

		Assert.Null(response.Error);
		Assert.NotNull(capturedPrompt);
		Assert.Contains("Image attachment (shot.png): ", capturedPrompt);
		Assert.Equal("fake-png-bytes", capturedImageContent);
	}

	[Fact]
	public async Task SendAsync_TextAttachment_InlinedUnderFencedSection()
	{
		string? capturedPrompt = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPrompt = ci.ArgAt<string?>(1);
				return new ProcessExecutionResult(0, "{\"result\":\"ok\"}", "", false);
			});

		var yamlBytes = Encoding.UTF8.GetBytes("wireframe: main\nkind: page\n");
		var attachment = new FrontierAttachment("text/plain", "spec.yaml", yamlBytes);
		var client = BuildClient(runner, command: "claude");
		await client.SendAsync(BuildRequest(attachments: new[] { attachment }));

		Assert.NotNull(capturedPrompt);
		Assert.Contains("--- spec.yaml ---", capturedPrompt);
		Assert.Contains("wireframe: main", capturedPrompt);
		Assert.Contains("--- end spec.yaml ---", capturedPrompt);
	}

	[Fact]
	public async Task SendAsync_ClaudeConcreteModel_AddsModelFlag()
	{
		ProcessStartInfo? capturedPsi = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPsi = ci.ArgAt<ProcessStartInfo>(0);
				return new ProcessExecutionResult(0, "{\"result\":\"ok\"}", "", false);
			});

		var client = BuildClient(runner, command: "claude", model: "claude-sonnet-4-6");
		await client.SendAsync(BuildRequest());

		Assert.NotNull(capturedPsi);
		Assert.Contains("--model", capturedPsi!.ArgumentList);
		Assert.Contains("claude-sonnet-4-6", capturedPsi.ArgumentList);
	}

	[Fact]
	public async Task SendAsync_ClaudePlaceholderModel_OmitsModelFlag()
	{
		// "(cli-managed)" or any "(...)" wrapped value is a placeholder and
		// must NOT result in a --model flag (let the CLI pick its default).
		ProcessStartInfo? capturedPsi = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPsi = ci.ArgAt<ProcessStartInfo>(0);
				return new ProcessExecutionResult(0, "{\"result\":\"ok\"}", "", false);
			});

		var client = BuildClient(runner, command: "claude", model: "(cli-managed)");
		await client.SendAsync(BuildRequest());

		Assert.NotNull(capturedPsi);
		Assert.DoesNotContain("--model", capturedPsi!.ArgumentList);
	}

	[Fact]
	public async Task SendAsync_CodexConcreteModel_AddsModelFlag()
	{
		ProcessStartInfo? capturedPsi = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPsi = ci.ArgAt<ProcessStartInfo>(0);
				return new ProcessExecutionResult(0, "codex-answer", "", false);
			});

		var client = BuildClient(runner, command: "codex", model: "gpt-5-codex");
		await client.SendAsync(BuildRequest());

		Assert.NotNull(capturedPsi);
		Assert.Contains("exec", capturedPsi!.ArgumentList);
		Assert.Contains("--skip-git-repo-check", capturedPsi.ArgumentList);
		Assert.Contains("--model", capturedPsi.ArgumentList);
		Assert.Contains("gpt-5-codex", capturedPsi.ArgumentList);
	}

	[Fact]
	public async Task SendAsync_UnknownCommand_PassesPromptAsPositionalArg()
	{
		ProcessStartInfo? capturedPsi = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				capturedPsi = ci.ArgAt<ProcessStartInfo>(0);
				return new ProcessExecutionResult(0, "unknown-cli output", "", false);
			});

		var client = BuildClient(runner, command: "my-custom-llm");
		await client.SendAsync(BuildRequest(system: "sys", user: "hello"));

		Assert.NotNull(capturedPsi);
		Assert.Single(capturedPsi!.ArgumentList);
		Assert.Contains("hello", capturedPsi.ArgumentList[0]!);
	}

	[Fact]
	public async Task SendAsync_TempWorkspace_CleanedUpAfterSuccess()
	{
		string? tempDirSeen = null;
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(ci =>
			{
				var prompt = ci.ArgAt<string?>(1) ?? string.Empty;
				var marker = "Image attachment (cleanup.png): ";
				var idx = prompt.IndexOf(marker, StringComparison.Ordinal);
				if (idx >= 0)
				{
					var path = prompt[(idx + marker.Length)..]
						.Split('\n', '\r')[0].Trim();
					tempDirSeen = Path.GetDirectoryName(path);
				}
				return new ProcessExecutionResult(0, "{\"result\":\"ok\"}", "", false);
			});

		var attachment = new FrontierAttachment("image/png", "cleanup.png",
			Encoding.UTF8.GetBytes("png"));
		var client = BuildClient(runner, command: "claude");
		await client.SendAsync(BuildRequest(attachments: new[] { attachment }));

		Assert.NotNull(tempDirSeen);
		Assert.False(Directory.Exists(tempDirSeen!),
			$"Temp workspace '{tempDirSeen}' should be cleaned up after SendAsync completes.");
	}

	[Fact]
	public async Task SendAsync_ClaudeJsonFencedResult_StripsCodeFence()
	{
		// Real-world Claude CLI output even with --output-format json sometimes
		// wraps the inner "result" payload in ```json ... ``` fences. The
		// validator downstream parses the extracted text as JSON, so we strip
		// fences inside the client.
		var runner = Substitute.For<IProcessRunner>();
		runner.RunAsync(Arg.Any<ProcessStartInfo>(), Arg.Any<string?>(),
			Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(new ProcessExecutionResult(
				ExitCode: 0,
				Stdout: "{\"type\":\"result\",\"result\":\"```json\\n{\\\"rating\\\":\\\"match\\\"}\\n```\"}",
				Stderr: string.Empty,
				TimedOut: false));

		var client = BuildClient(runner, command: "claude");
		var response = await client.SendAsync(BuildRequest());

		Assert.Null(response.Error);
		Assert.Equal("{\"rating\":\"match\"}", response.Text);
	}

	[Fact]
	public void StripCodeFence_HandlesFencedAndUnfencedInputs()
	{
		Assert.Equal("{\"a\":1}", CliFrontierClient.StripCodeFence("{\"a\":1}"));
		Assert.Equal("{\"a\":1}", CliFrontierClient.StripCodeFence("```json\n{\"a\":1}\n```"));
		Assert.Equal("{\"a\":1}", CliFrontierClient.StripCodeFence("```\n{\"a\":1}\n```"));
		Assert.Equal("{\"a\":1}", CliFrontierClient.StripCodeFence("  ```json\n{\"a\":1}\n```  "));
	}
}
