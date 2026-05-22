using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitWorkspaceDiscoveryTests
{
	[Fact]
	public void Discover_RecursivelyFindsPackageAndProjectReferencedAiUnitProjects()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProject(
			"tests/App.Tests/App.Tests.csproj",
			"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <AssemblyName>App.Tests</AssemblyName>
			  </PropertyGroup>
			  <ItemGroup>
			    <PackageReference Include="SharpNinja.aiUnit" Version="0.5.0-beta" />
			  </ItemGroup>
			</Project>
			""");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", "codex", "claude"));
		workspace.WriteProject(
			"nested/ProjectRef.Tests/ProjectRef.Tests.csproj",
			"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			    <ProjectReference Include="..\..\src\SharpNinja.AiUnit\SharpNinja.AiUnit.csproj" />
			  </ItemGroup>
			</Project>
			""");
		workspace.WriteConfig("nested/ProjectRef.Tests/appsettings.aiunit.json", Config("claude", "claude"));
		workspace.WriteProject(
			"src/Plain/Plain.csproj",
			"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""");

		var result = AiUnitWorkspaceDiscovery.Discover(workspace.Root);

		Assert.Equal(2, result.Projects.Count);
		var packageProject = Assert.Single(result.Projects, project => project.ProjectName == "App.Tests");
		Assert.True(packageProject.HasPackageReference);
		Assert.False(packageProject.HasProjectReference);
		Assert.Equal("codex", packageProject.ActiveStrategy);
		Assert.Equal(2, packageProject.StrategyCount);
		Assert.Equal(AiUnitDiscoveryStatus.Ok, packageProject.Status);

		var projectRef = Assert.Single(result.Projects, project => project.ProjectName == "ProjectRef.Tests");
		Assert.False(projectRef.HasPackageReference);
		Assert.True(projectRef.HasProjectReference);
		Assert.Equal("project", projectRef.ReferenceSummary);
	}

	[Fact]
	public void Discover_MissingConfig_ReturnsWarningForAiUnitReferencedProject()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProject(
			"tests/MissingConfig.Tests/MissingConfig.Tests.csproj",
			"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <ItemGroup>
			    <PackageReference Include="SharpNinja.aiUnit" Version="0.5.0-beta" />
			  </ItemGroup>
			</Project>
			""");

		var project = Assert.Single(AiUnitWorkspaceDiscovery.Discover(workspace.Root).Projects);

		Assert.Equal(AiUnitDiscoveryStatus.Warning, project.Status);
		Assert.Null(project.ConfigPath);
		Assert.Contains(project.Messages, message => message.Contains("was not found", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void Discover_MalformedConfig_ReturnsError()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Bad.Tests/Bad.Tests.csproj");
		workspace.WriteConfig("tests/Bad.Tests/appsettings.aiunit.json", "{ not-json");

		var project = Assert.Single(AiUnitWorkspaceDiscovery.Discover(workspace.Root).Projects);

		Assert.Equal(AiUnitDiscoveryStatus.Error, project.Status);
		Assert.Contains(project.Messages, message => message.Contains("Malformed config", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void Discover_DuplicateStrategyNames_ReturnsError()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Duplicate.Tests/Duplicate.Tests.csproj");
		workspace.WriteConfig(
			"tests/Duplicate.Tests/appsettings.aiunit.json",
			"""
			{
			  "AiUnit": {
			    "ActiveStrategy": "codex",
			    "Strategies": {
			      "codex": { "Kind": "cli", "Command": "codex" },
			      "Codex": { "Kind": "cli", "Command": "codex" }
			    }
			  }
			}
			""");

		var project = Assert.Single(AiUnitWorkspaceDiscovery.Discover(workspace.Root).Projects);

		Assert.Equal(AiUnitDiscoveryStatus.Error, project.Status);
		Assert.Contains(project.Messages, message => message.Contains("Duplicate strategy name 'codex'", StringComparison.Ordinal));
	}

	[Fact]
	public void Discover_NoProjectRoot_ReturnsEmptyResult()
	{
		using var workspace = TempWorkspace.Create();

		var result = AiUnitWorkspaceDiscovery.Discover(workspace.Root);

		Assert.Empty(result.Projects);
		Assert.False(result.HasErrors);
	}

	[Fact]
	public async Task ExecuteAsync_Scan_PrintsDiscoveredProjectSummary()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", "codex", "claude"));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "scan", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("projects: 1", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("[ok] App.Tests", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("active: codex", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("strategies: 2", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("refs: package", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_Show_PrintsProjectDetails()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", "codex", "claude"));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "show", "App.Tests", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("App.Tests", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("refs: package", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("active: codex", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("    - claude", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("    - codex", stdout.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_Validate_ReturnsNonZeroForConfigErrors()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/Bad.Tests/Bad.Tests.csproj");
		workspace.WriteConfig("tests/Bad.Tests/appsettings.aiunit.json", "{ not-json");
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "validate", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(1, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("[error] Bad.Tests", stdout.ToString(), StringComparison.Ordinal);
		Assert.Contains("Malformed config", stdout.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task ExecuteAsync_Validate_ReturnsNonZeroForNoProjectRoot()
	{
		using var workspace = TempWorkspace.Create();
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "validate", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(1, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("no aiUnit-enabled projects found", stdout.ToString(), StringComparison.Ordinal);
	}

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
			new(Path.Combine(Path.GetTempPath(), "aiunit-repl-" + Guid.NewGuid().ToString("N")));

		public void WriteProjectWithPackage(string relativePath) =>
			WriteProject(
				relativePath,
				"""
				<Project Sdk="Microsoft.NET.Sdk">
				  <ItemGroup>
				    <PackageReference Include="SharpNinja.aiUnit" Version="0.5.0-beta" />
				  </ItemGroup>
				</Project>
				""");

		public void WriteProject(string relativePath, string content) =>
			WriteFile(relativePath, content);

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
