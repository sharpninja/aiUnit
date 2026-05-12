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
	/// Walks up from AppContext.BaseDirectory looking for a folder named
	/// "SharpNinja.aiUnit" containing a "src" subfolder. Returns the resolved
	/// "src" path. Tests run from
	/// libs/SharpNinja.aiUnit/tests/.../bin/Debug/net10.0/, so the walk only
	/// needs to climb a handful of levels.
	/// </summary>
	private static string FindSrcRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			if (string.Equals(dir.Name, "SharpNinja.aiUnit", StringComparison.OrdinalIgnoreCase))
			{
				var candidate = Path.Combine(dir.FullName, "src");
				if (Directory.Exists(candidate))
				{
					return candidate;
				}
			}
			dir = dir.Parent;
		}
		throw new InvalidOperationException(
			$"Could not locate SharpNinja.aiUnit/src walking up from {AppContext.BaseDirectory}");
	}
}
