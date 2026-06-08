using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitReplCommandLineTests
{
	[Fact]
	public void ToolProject_Metadata_PackagesAsAiUnitDotNetTool()
	{
		var projectPath = Path.Combine(
			FindRepoRoot(),
			"src",
			"SharpNinja.AiUnit.Repl",
			"SharpNinja.AiUnit.Repl.csproj");
		var project = XDocument.Load(projectPath);

		Assert.Equal("Exe", ProjectProperty(project, "OutputType"));
		Assert.Equal("true", ProjectProperty(project, "PackAsTool"));
		Assert.Equal("aiunit", ProjectProperty(project, "ToolCommandName"));
		Assert.Equal("SharpNinja.aiUnit.Tool", ProjectProperty(project, "PackageId"));
		Assert.DoesNotContain(project.Descendants("Version"), element => element.Parent?.Name == "PropertyGroup");
		// GitVersion.MsBuild is injected for all projects via Directory.Build.props (no explicit ref needed in individual .csproj files)
		Assert.Contains(
			project.Descendants("ProjectReference"),
			reference => reference.Attribute("Include")?.Value == @"..\SharpNinja.AiUnit\SharpNinja.AiUnit.csproj");
	}

	[Fact]
	public async Task ExecuteAsync_Help_PrintsSupportedCommands()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(new[] { "--help" }, stdout, stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("Usage:", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("repl", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("tui", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("scan", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("--version", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_Version_PrintsToolVersion()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(new[] { "--version" }, stdout, stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Matches(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$", AiUnitReplCommandLine.ToolVersion);
		Assert.DoesNotContain("+", AiUnitReplCommandLine.ToolVersion, StringComparison.Ordinal);
		Assert.Equal($"{AiUnitReplCommandLine.ToolVersion}{Environment.NewLine}", stdout.ToString());
	}

	[Fact]
	public void Parse_NoArguments_DefaultsToReplMode()
	{
		var result = AiUnitReplCommandLine.Parse(Array.Empty<string>());

		Assert.True(result.Success);
		Assert.Equal(AiUnitReplMode.Repl, result.Options!.Mode);
		Assert.False(result.Options.ShowHelp);
		Assert.False(result.Options.ShowVersion);
	}

	[Theory]
	[InlineData("repl", AiUnitReplMode.Repl)]
	[InlineData("tui", AiUnitReplMode.Tui)]
	[InlineData("scan", AiUnitReplMode.Scan)]
	public void Parse_PositionalCommand_SelectsMode(string command, AiUnitReplMode expectedMode)
	{
		var result = AiUnitReplCommandLine.Parse(new[] { command });

		Assert.True(result.Success);
		Assert.Equal(expectedMode, result.Options!.Mode);
	}

	[Theory]
	[InlineData("--mode", "tui", AiUnitReplMode.Tui)]
	[InlineData("--mode", "scan", AiUnitReplMode.Scan)]
	public void Parse_ModeOption_SelectsMode(string option, string value, AiUnitReplMode expectedMode)
	{
		var result = AiUnitReplCommandLine.Parse(new[] { option, value });

		Assert.True(result.Success);
		Assert.Equal(expectedMode, result.Options!.Mode);
	}

	[Fact]
	public async Task ExecuteAsync_Tui_DispatchesShellMode()
	{
		var workspacePath = Path.Combine(Path.GetTempPath(), "aiunit-cli-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(workspacePath);
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		try
		{
			var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
				new[] { "tui", "--workspace", workspacePath },
				stdout,
				stderr);

			Assert.Equal(0, exitCode);
			Assert.Equal(string.Empty, stderr.ToString());
			Assert.Contains("aiunit tui - Workspace Overview", stdout.ToString(), StringComparison.Ordinal);
			Assert.Contains($"root: {workspacePath}", stdout.ToString(), StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(workspacePath, recursive: true);
		}
	}

	[Fact]
	public async Task ExecuteAsync_Repl_ProcessesScriptedCommands()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", "codex", "claude"));
		using var stdin = new StringReader(
			"list" + Environment.NewLine +
			"show App.Tests" + Environment.NewLine +
			"exit" + Environment.NewLine);
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "repl", "--workspace", workspace.Root },
			stdin,
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("aiunit repl", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("App.Tests | active: codex | status: ok", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("refs: package", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("    - claude", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("    - codex", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("bye", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_Repl_ReportsInvalidCommandAndContinues()
	{
		using var stdin = new StringReader(
			"unknown" + Environment.NewLine +
			"version" + Environment.NewLine +
			"exit" + Environment.NewLine);
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "repl" },
			stdin,
			stdout,
			stderr);

		Assert.Equal(2, exitCode);
		Assert.Contains("Unknown aiUnit command 'unknown'.", stderr.ToString(), StringComparison.Ordinal);
		Assert.Contains(AiUnitReplCommandLine.ToolVersion, stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("bye", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_InvalidMode_ReturnsNonZeroAndHelp()
	{
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(new[] { "--mode", "bogus" }, stdout, stderr);

		Assert.Equal(2, exitCode);
		Assert.Equal(string.Empty, stdout.ToString());
		Assert.Contains("Unsupported aiUnit mode 'bogus'", stderr.ToString(), StringComparison.Ordinal);
		Assert.Contains("Usage:", stderr.ToString(), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("set-active foo --project bar")]
	[InlineData("add-strategy mystrat --project p1 --kind openai-compatible")]
	[InlineData("edit-strategy mystrat --project p1")]
	[InlineData("remove-strategy mystrat --project p1")]
	[InlineData("export --project p1")]
	public void Parse_AliasCommands_RecognizeNewModes(string command)
	{
		var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var result = AiUnitReplCommandLine.Parse(args);

		Assert.True(result.Success, $"Parse failed for '{command}': {result.Error}");
		// Dispatch verified in Execute tests or manual; Parse now recognizes per FR-AIUNITREPL-003
	}

	private static string FindRepoRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "SharpNinja.aiUnit.sln")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException("Could not locate repository root from test output directory.");
	}

	private static string ProjectProperty(XContainer project, string name) =>
		project.Descendants(name).Single().Value;

	private static string Config(string activeStrategy, params string[] strategies)
	{
		var strategyJson = string.Join(
			$",{Environment.NewLine}",
			strategies.Select(strategy => $"      \"{strategy}\": {{ \"Kind\": \"cli\", \"Command\": \"{strategy}\" }}"));

		return
			"{" + Environment.NewLine +
			"  \"AiUnit\": {" + Environment.NewLine +
			$"    \"ActiveStrategy\": \"{activeStrategy}\"," + Environment.NewLine +
			"    \"Strategies\": {" + Environment.NewLine +
			strategyJson + Environment.NewLine +
			"    }" + Environment.NewLine +
			"  }" + Environment.NewLine +
			"}";
	}

	private sealed class TempWorkspace : IDisposable
	{
		private TempWorkspace(string root)
		{
			Root = root;
		}

		public string Root { get; }

		public static TempWorkspace Create() =>
			new(Path.Combine(Path.GetTempPath(), "aiunit-cli-" + Guid.NewGuid().ToString("N")));

		public void WriteProjectWithPackage(string relativePath, string assemblyName) =>
			WriteFile(
				relativePath,
				$$"""
				<Project Sdk="Microsoft.NET.Sdk">
				  <PropertyGroup>
				    <AssemblyName>{{assemblyName}}</AssemblyName>
				  </PropertyGroup>
				  <ItemGroup>
				    <PackageReference Include="SharpNinja.aiUnit" Version="0.5.0-beta" />
				  </ItemGroup>
				</Project>
				""");

		public void WriteConfig(string relativePath, string content) =>
			WriteFile(relativePath, content);

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}

		private void WriteFile(string relativePath, string content)
		{
			var path = Path.Combine(Root, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
		}
	}
}
