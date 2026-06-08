using System;
using System.IO;
using SharpNinja.AiUnit.Desktop;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Desktop;

public sealed class ProgramCommandLineTests
{
    [Fact]
    public void Version_PrintsVersionAndDoesNotStartGui()
    {
        var (exitCode, output) = RunWithCapturedOutput("--version");

        Assert.Equal(0, exitCode);
        Assert.Contains("0.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting Avalonia UI", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_PrintsUsageAndDoesNotStartGui()
    {
        var (exitCode, output) = RunWithCapturedOutput("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage: aiunit-review", output, StringComparison.Ordinal);
        Assert.Contains("--probe-exit", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting Avalonia UI", output, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunWithCapturedOutput(params string[] args)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);
            var exitCode = Program.Main(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
