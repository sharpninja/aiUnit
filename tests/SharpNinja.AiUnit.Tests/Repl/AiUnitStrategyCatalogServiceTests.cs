using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitStrategyCatalogServiceTests
{
	[Fact]
	public void Catalog_CollectsStrategiesAcrossDiscoveredProjects()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/AppA/AppA.csproj", "AppA");
		workspace.WriteConfig("tests/AppA/appsettings.aiunit.json", Config("codex", ("codex", "codex"), ("claude", "claude")));
		workspace.WriteProjectWithPackage("tests/AppB/AppB.csproj", "AppB");
		workspace.WriteConfig("tests/AppB/appsettings.aiunit.json", Config("gemini", ("gemini", "gemini")));

		var entries = new AiUnitStrategyCatalogService().Catalog(workspace.Root);

		Assert.Equal(3, entries.Count);
		var codex = Assert.Single(entries, entry => entry.StrategyName == "codex");
		Assert.Equal("AppA", codex.ProjectName);
		Assert.True(codex.IsActive);
		Assert.Equal("cli", codex.Definition.Kind);
		Assert.Equal("codex", codex.Definition.Command);
	}

	[Fact]
	public void Apply_DryRunReportsAddsAndConflictsWithoutChangingFiles()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Source/Source.csproj", "Source");
		workspace.WriteConfig("tests/Source/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		workspace.WriteProjectWithPackage("tests/AddTarget/AddTarget.csproj", "AddTarget");
		workspace.WriteConfig("tests/AddTarget/appsettings.aiunit.json", Config("claude", ("claude", "claude")));
		workspace.WriteProjectWithPackage("tests/ConflictTarget/ConflictTarget.csproj", "ConflictTarget");
		workspace.WriteConfig("tests/ConflictTarget/appsettings.aiunit.json", Config("codex", ("codex", "different-codex")));
		var before = File.ReadAllText(workspace.Path("tests/ConflictTarget/appsettings.aiunit.json"));

		var result = new AiUnitStrategyCatalogService().Apply(
			workspace.Root,
			"codex",
			sourceProjectName: "Source",
			targetProjectNames: null,
			dryRun: true,
			force: false);

		Assert.True(result.HasConflicts);
		Assert.Contains(result.Projects, project => project.ProjectName == "AddTarget" && project.Changed && project.Message.Contains("Would add", StringComparison.Ordinal));
		Assert.Contains(result.Projects, project => project.ProjectName == "ConflictTarget" && project.Conflict);
		Assert.Equal(before, File.ReadAllText(workspace.Path("tests/ConflictTarget/appsettings.aiunit.json")));
		Assert.False(Directory.Exists(workspace.Path("tests/AddTarget/.aiunit")));
	}

	[Fact]
	public void Apply_SelectedProjectWritesSnapshotAndAddsStrategy()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Source/Source.csproj", "Source");
		workspace.WriteConfig("tests/Source/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		workspace.WriteProjectWithPackage("tests/Target/Target.csproj", "Target");
		workspace.WriteConfig("tests/Target/appsettings.aiunit.json", Config("claude", ("claude", "claude")));

		var result = new AiUnitStrategyCatalogService().Apply(
			workspace.Root,
			"codex",
			sourceProjectName: "Source",
			targetProjectNames: new[] { "Target" },
			dryRun: false,
			force: false);

		var target = Assert.Single(result.Projects);
		Assert.True(target.Changed);
		Assert.False(target.Conflict);
		Assert.True(File.Exists(target.SnapshotPath));
		var strategies = JsonNode.Parse(File.ReadAllText(workspace.Path("tests/Target/appsettings.aiunit.json")))!
			["AiUnit"]!["Strategies"]!;
		Assert.NotNull(strategies["claude"]);
		Assert.Equal("codex", strategies["codex"]!["Command"]!.GetValue<string>());
	}

	[Fact]
	public void Apply_ForceOverwritesConflictingStrategyWithSnapshot()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Source/Source.csproj", "Source");
		workspace.WriteConfig("tests/Source/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		workspace.WriteProjectWithPackage("tests/Target/Target.csproj", "Target");
		workspace.WriteConfig("tests/Target/appsettings.aiunit.json", Config("codex", ("codex", "different-codex")));

		var result = new AiUnitStrategyCatalogService().Apply(
			workspace.Root,
			"codex",
			sourceProjectName: "Source",
			targetProjectNames: new[] { "Target" },
			dryRun: false,
			force: true);

		var target = Assert.Single(result.Projects);
		Assert.True(target.Changed);
		Assert.False(target.Conflict);
		Assert.True(File.Exists(target.SnapshotPath));
		var codex = JsonNode.Parse(File.ReadAllText(workspace.Path("tests/Target/appsettings.aiunit.json")))!
			["AiUnit"]!["Strategies"]!["codex"]!;
		Assert.Equal("codex", codex["Command"]!.GetValue<string>());
	}

	[Fact]
	public async Task ExecuteAsync_Catalog_PrintsDiscoveredStrategies()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/AppA/AppA.csproj", "AppA");
		workspace.WriteConfig("tests/AppA/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "catalog", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("codex | AppA | cli | active", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_ApplyGlobalDryRun_ReturnsConflictExitCode()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Source/Source.csproj", "Source");
		workspace.WriteConfig("tests/Source/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		workspace.WriteProjectWithPackage("tests/Target/Target.csproj", "Target");
		workspace.WriteConfig("tests/Target/appsettings.aiunit.json", Config("codex", ("codex", "different-codex")));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "apply-global", "codex", "--from", "Source", "--workspace", workspace.Root, "--dry-run" },
			stdout,
			stderr);

		Assert.Equal(1, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("dry-run: true", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("[conflict] Target", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_Restore_RestoresLatestProjectSnapshot()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Target/Target.csproj", "Target");
		workspace.WriteConfig("tests/Target/appsettings.aiunit.json", Config("claude", ("claude", "claude")));
		var configPath = workspace.Path("tests/Target/appsettings.aiunit.json");
		var expected = File.ReadAllText(configPath);
		new AiUnitConfigSnapshotService().CreateSnapshot(configPath);
		File.WriteAllText(configPath, Config("codex", ("codex", "codex")));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "restore", "Target", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Equal(expected, File.ReadAllText(configPath));
		Assert.Contains("restored Target", stdout.ToString(), StringComparison.Ordinal);
	}

	private static string Config(string activeStrategy, params (string Name, string Command)[] strategies)
	{
		var strategyJson = string.Join(
			$",{Environment.NewLine}",
			strategies.Select(strategy => $"      \"{strategy.Name}\": {{ \"Kind\": \"cli\", \"Command\": \"{strategy.Command}\" }}"));

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
			new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aiunit-catalog-" + Guid.NewGuid().ToString("N")));

		public string Path(string relativePath) =>
			System.IO.Path.Combine(Root, relativePath);

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
			var path = Path(relativePath);
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
		}
	}
}
