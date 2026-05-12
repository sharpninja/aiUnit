using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Guard;

/// <summary>
/// Build-time guard: walks every .cs file under libs/SharpNinja.aiUnit/src/**
/// looking for the substring "TruckMate." and fails the build on any hit.
/// Test fixtures under tests/ are intentionally excluded - the consumer
/// migration period may legitimately reference TruckMate during a transition
/// window, but the library surface must remain pristine.
/// </summary>
public class NoTruckMateLeakTests
{
	[Fact]
	public void NoTruckMateLeak_GuardTest()
	{
		var srcRoot = FindSrcRoot();
		Assert.True(Directory.Exists(srcRoot), $"src root not located: {srcRoot}");

		var leaks = Directory
			.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
			.Where(static path => File.ReadAllText(path).Contains("TruckMate.", StringComparison.Ordinal))
			.ToList();

		Assert.True(
			leaks.Count == 0,
			"Detected TruckMate. references under src/: " + string.Join("; ", leaks));
	}

	/// <summary>
	/// Walks up from AppContext.BaseDirectory looking for the SharpNinja.aiUnit
	/// repo root. Two layouts are supported so the guard test passes both
	/// in-place (as a subtree under TruckMate at libs/SharpNinja.aiUnit/) and
	/// after detach (as a standalone repo where the root contains src/ + tests/
	/// + SharpNinja.aiUnit.sln). The walker accepts either:
	///   1. a parent folder literally named "SharpNinja.aiUnit" with a "src" child, OR
	///   2. any folder containing "src/SharpNinja.AiUnit" (the project dir name).
	/// </summary>
	private static string FindSrcRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			// Subtree layout: parent named "SharpNinja.aiUnit" with src/ child.
			if (string.Equals(dir.Name, "SharpNinja.aiUnit", StringComparison.OrdinalIgnoreCase))
			{
				var candidate = Path.Combine(dir.FullName, "src");
				if (Directory.Exists(candidate))
				{
					return candidate;
				}
			}

			// Detach layout: any ancestor containing src/SharpNinja.AiUnit/.
			var projectMarker = Path.Combine(dir.FullName, "src", "SharpNinja.AiUnit");
			if (Directory.Exists(projectMarker))
			{
				return Path.Combine(dir.FullName, "src");
			}

			dir = dir.Parent;
		}
		throw new InvalidOperationException(
			$"Could not locate SharpNinja.aiUnit src walking up from {AppContext.BaseDirectory}");
	}
}
