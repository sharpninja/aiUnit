using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Review;

/// <summary>
/// WS-A regression guard: the retired public <c>AIUNIT_GROK_MODEL</c> variable must
/// not reappear anywhere in product source. The Grok bridge resolves its model from
/// the shared <c>AIUNIT_MODEL</c> / <c>AIUNIT_MODEL_VERSION</c> path like every other
/// strategy, so any reference to a separate Grok-specific model variable is a
/// normalization regression.
/// </summary>
public sealed class GrokModelVariableGuardTests
{
	private const string RetiredVariable = "AIUNIT_GROK_MODEL";

	[Fact]
	public void Source_DoesNotReferenceRetiredAiUnitGrokModelVariable()
	{
		var srcRoot = LocateSrcRoot();

		var offenders = Directory
			.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
			.Where(static path => !IsGeneratedOrBuildOutput(path))
			.Where(path => File.ReadAllText(path).Contains(RetiredVariable, StringComparison.Ordinal))
			.ToArray();

		Assert.True(
			offenders.Length == 0,
			$"{RetiredVariable} must not appear in product source; resolve the Grok model "
			+ "from AIUNIT_MODEL/AIUNIT_MODEL_VERSION instead. Offending files: "
			+ string.Join(", ", offenders));
	}

	private static bool IsGeneratedOrBuildOutput(string path)
	{
		var sep = Path.DirectorySeparatorChar;
		return path.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
			|| path.Contains($"{sep}bin{sep}", StringComparison.Ordinal);
	}

	private static string LocateSrcRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var src = Path.Combine(dir.FullName, "src");
			if (Directory.Exists(Path.Combine(src, "SharpNinja.AiUnit.GrokBridge")))
			{
				return src;
			}

			dir = dir.Parent;
		}

		throw new DirectoryNotFoundException(
			"Could not locate the repository 'src' directory from " + AppContext.BaseDirectory);
	}
}
