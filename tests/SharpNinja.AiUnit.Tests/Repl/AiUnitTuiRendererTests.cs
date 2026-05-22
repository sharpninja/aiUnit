using System;
using System.IO;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitTuiRendererTests
{
	[Theory]
	[InlineData(null, AiUnitTuiScreen.Overview)]
	[InlineData("overview", AiUnitTuiScreen.Overview)]
	[InlineData("projects", AiUnitTuiScreen.Projects)]
	[InlineData("catalog", AiUnitTuiScreen.Catalog)]
	[InlineData("validate", AiUnitTuiScreen.Validate)]
	public void ParseScreen_MapsKnownScreenIds(string? screenId, AiUnitTuiScreen expected)
	{
		Assert.Equal(expected, AiUnitTuiRenderer.ParseScreen(screenId));
	}

	[Fact]
	public void RenderToText_OverviewIncludesWireframeSections()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", ("codex", "codex"), ("claude", "claude")));

		var text = AiUnitTuiRenderer.RenderToText(workspace.Root, "overview");

		Assert.Contains("aiunit tui - Workspace Overview", text, StringComparison.Ordinal);
		Assert.Contains("> Overview", text, StringComparison.Ordinal);
		Assert.Contains("Discovered aiUnit Projects", text, StringComparison.Ordinal);
		Assert.Contains("App.Tests", text, StringComparison.Ordinal);
		Assert.Contains("Scan Summary", text, StringComparison.Ordinal);
	}

	[Fact]
	public void RenderToText_ProjectEditorIncludesStrategyActions()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", ("codex", "codex"), ("claude", "claude")));

		var text = AiUnitTuiRenderer.RenderToText(workspace.Root, "projects");

		Assert.Contains("aiunit tui - Project Strategy Editor", text, StringComparison.Ordinal);
		Assert.Contains("> Projects", text, StringComparison.Ordinal);
		Assert.Contains("Selected Strategy: codex", text, StringComparison.Ordinal);
		Assert.Contains("A Add strategy", text, StringComparison.Ordinal);
		Assert.Contains("R Restore snapshot", text, StringComparison.Ordinal);
	}

	[Fact]
	public void RenderToText_CatalogIncludesApplyPreview()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", ("codex", "codex")));

		var text = AiUnitTuiRenderer.RenderToText(workspace.Root, "catalog");

		Assert.Contains("aiunit tui - Strategy Catalog", text, StringComparison.Ordinal);
		Assert.Contains("> Catalog", text, StringComparison.Ordinal);
		Assert.Contains("Reusable Strategies", text, StringComparison.Ordinal);
		Assert.Contains("Apply Preview", text, StringComparison.Ordinal);
		Assert.Contains("apply-global codex --dry-run", text, StringComparison.Ordinal);
	}

	[Fact]
	public void RenderToText_ValidationIncludesGateStates()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", ("codex", "codex")));

		var text = AiUnitTuiRenderer.RenderToText(workspace.Root, "validate");

		Assert.Contains("aiunit tui - Validation and Deploy", text, StringComparison.Ordinal);
		Assert.Contains("> Validate", text, StringComparison.Ordinal);
		Assert.Contains("Validation and Deploy", text, StringComparison.Ordinal);
		Assert.Contains("capture TUI screenshots", text, StringComparison.Ordinal);
		Assert.Contains("aiUnit wireframe comparisons", text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExecuteAsync_TuiScreen_PrintsSelectedScreen()
	{
		using var workspace = TempWorkspace.Create();
		workspace.WriteProjectWithPackage("tests/App.Tests/App.Tests.csproj", "App.Tests");
		workspace.WriteConfig("tests/App.Tests/appsettings.aiunit.json", Config("codex", ("codex", "codex")));
		using var stdout = new StringWriter();
		using var stderr = new StringWriter();

		var exitCode = await AiUnitReplCommandLine.ExecuteAsync(
			new[] { "tui", "catalog", "--workspace", workspace.Root },
			stdout,
			stderr);

		Assert.Equal(0, exitCode);
		Assert.Equal(string.Empty, stderr.ToString());
		Assert.Contains("aiunit tui - Strategy Catalog", stdout.ToString(), StringComparison.Ordinal);
	}

	private static string Config(string activeStrategy, params (string Name, string Command)[] strategies)
	{
		var strategyJson = string.Join(
			$",{Environment.NewLine}",
			Array.ConvertAll(strategies, strategy => $"      \"{strategy.Name}\": {{ \"Kind\": \"cli\", \"Command\": \"{strategy.Command}\" }}"));

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
			new(Path.Combine(Path.GetTempPath(), "aiunit-tui-" + Guid.NewGuid().ToString("N")));

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
