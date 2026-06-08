using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Build;

public sealed class BuildVersioningTests
{
	[Fact]
	public void NukeBuild_Appends_BuildNumber_To_Normal_GitVersion_PackageVersion()
	{
		var source = ReadBuildSource();

		Assert.Contains("GitVersion.NuGetVersionV2", source);
		Assert.Contains("GitVersion.FullSemVer", source);
		Assert.Contains("AppendBuildNumberToNuGetVersion(baseVersion, EffectiveBuildNumber)", source);
		Assert.Contains("return baseVersion.Contains('-')", source);
		Assert.Contains("DateTimeOffset.UtcNow.ToString(\"yyyyMMddHHmmss\"", source);
	}

	[Fact]
	public void NukeBuild_Uses_EffectiveVersion_For_Build_And_Pack()
	{
		var source = ReadBuildSource();

		Assert.Contains(".SetVersion(EffectivePackageVersion)", source);
		Assert.Contains(".SetProperty(\"PackageVersion\", EffectivePackageVersion)", source);
		Assert.Contains(".SetInformationalVersion(EffectiveInformationalVersion)", source);
		Assert.Contains(".SetProperty(\"DisableGitVersionTask\", true)", source);
		Assert.Contains(".SetProperty(\"UpdateVersionProperties\", false)", source);
		Assert.DoesNotContain("var pkgVer = !string.IsNullOrWhiteSpace(Version) ? Version : GitVersion.NuGetVersionV2", source);
	}

	[Fact]
	public void NukeBuild_Selects_Most_Recently_Written_Nupkg_For_Redeploy()
	{
		var source = ReadBuildSource();

		Assert.Contains("OrderByDescending(x => File.GetLastWriteTimeUtc(x))", source);
		Assert.Contains("ThenByDescending(x => x.Name)", source);
	}

	[Fact]
	public void NukeBuild_PackTargets_Build_With_The_Same_Version_They_Package()
	{
		var source = ReadBuildSource();
		var packBlocks = Regex.Matches(source, @"DotNetPack\(s =>\s*\{.*?return s;", RegexOptions.Singleline);

		Assert.True(packBlocks.Count >= 3, "Expected library and two tool pack blocks.");
		Assert.All(packBlocks, block => Assert.DoesNotContain(".SetNoBuild(true)", block.Value));
		Assert.Contains(".SetNoRestore(true)", source);
	}

	private static string ReadBuildSource()
	{
		var root = FindRepoRoot();
		return File.ReadAllText(Path.Combine(root, "_build", "Build.cs"));
	}

	private static string FindRepoRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "SharpNinja.aiUnit.sln")))
				return dir.FullName;
			dir = dir.Parent;
		}

		return AppContext.BaseDirectory;
	}
}
