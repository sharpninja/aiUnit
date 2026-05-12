using System;
using System.IO;
using System.Linq;
using SharpNinja.AiUnit.Scenarios;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Scenarios;

/// <summary>
/// Phase-4 scenario-catalog coverage: confirms the walker can find a
/// consumer-provided marker folder by climbing parent directories starting
/// at <see cref="AppContext.BaseDirectory"/>. The catalog itself stays
/// YamlDotNet-free; the loader function the consumer supplies is what
/// actually parses the YAML.
/// </summary>
public class AiUnitScenarioCatalogTests
{
	/// <summary>
	/// Drops a temp marker folder containing a fake YAML file beside the
	/// test output directory's grandparent, calls
	/// <see cref="AiUnitScenarioCatalog.LoadAll"/> with a pass-through
	/// loader, and verifies the file is found.
	/// </summary>
	[Fact]
	public void Walker_LocatesMarkerFolder_FromBaseDirectory()
	{
		// Create a marker folder under a temp location, then point
		// AppContext.BaseDirectory traversal at it by parking the marker
		// in a temp directory and instructing the catalog where to anchor
		// via the BaseDirectory override. (We use the unique-name marker
		// so we cannot collide with the real "wireframes"/"scenarios" of
		// the consuming repo if a tester runs this manually.)
		var unique = "aiunit-test-" + Guid.NewGuid().ToString("n").Substring(0, 12);
		var root = Path.Combine(Path.GetTempPath(), "aiunit-tests", unique);
		var marker = Path.Combine(root, "marker-" + unique);
		Directory.CreateDirectory(marker);
		var yamlPath = Path.Combine(marker, "sample.yaml");
		File.WriteAllText(yamlPath, "id: sample\nname: hello\n");

		try
		{
			// startPath is somewhere INSIDE root (a fake "deep" subfolder),
			// so the walker climbs up and finds marker by name.
			var deep = Path.Combine(root, "a", "b", "c");
			Directory.CreateDirectory(deep);

			var found = AiUnitScenarioCatalog.LoadAll<(string Path, string Text)>(
				markerFolderName: Path.GetFileName(marker),
				loader: (path, text) => (path, text),
				startDirectory: deep);

			Assert.NotEmpty(found);
			Assert.Single(found, x => string.Equals(
				Path.GetFullPath(x.Path),
				Path.GetFullPath(yamlPath),
				StringComparison.OrdinalIgnoreCase));
			Assert.Contains(found, x => x.Text.Contains("hello", StringComparison.Ordinal));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
		}
	}
}
