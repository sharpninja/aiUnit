using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SharpNinja.AiUnit.Repl;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Repl;

public sealed class AiUnitConfigSnapshotServiceTests
{
	private static readonly DateTimeOffset FirstTimestamp =
		new(2026, 5, 22, 20, 36, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset SecondTimestamp =
		new(2026, 5, 22, 20, 37, 0, TimeSpan.Zero);

	[Fact]
	public void CreateSnapshot_WritesVerifiedCopyBesideConfig()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();

		var snapshot = service.CreateSnapshot(workspace.Path, FirstTimestamp);

		Assert.True(File.Exists(snapshot.SnapshotPath));
		Assert.Equal(Path.Combine(workspace.Root, ".aiunit", "snapshots"), Path.GetDirectoryName(snapshot.SnapshotPath));
		Assert.Equal(File.ReadAllText(workspace.Path), File.ReadAllText(snapshot.SnapshotPath));
		Assert.Equal(new FileInfo(workspace.Path).Length, snapshot.Bytes);
		Assert.Equal(Sha256(File.ReadAllBytes(workspace.Path)), snapshot.Sha256);
	}

	[Fact]
	public void CreateSnapshot_DoesNotOverwriteExistingSnapshotForSameTimestamp()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();

		var first = service.CreateSnapshot(workspace.Path, FirstTimestamp);
		var second = service.CreateSnapshot(workspace.Path, FirstTimestamp);

		Assert.NotEqual(first.SnapshotPath, second.SnapshotPath);
		Assert.True(File.Exists(first.SnapshotPath));
		Assert.True(File.Exists(second.SnapshotPath));
		Assert.EndsWith("-1.json", second.SnapshotPath, StringComparison.Ordinal);
	}

	[Fact]
	public void RestoreSnapshot_RestoresExactBytesFromSelectedSnapshot()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();
		var original = File.ReadAllText(workspace.Path);
		var snapshot = service.CreateSnapshot(workspace.Path, FirstTimestamp);
		File.WriteAllText(workspace.Path, Config("codex"));

		var result = service.RestoreSnapshot(workspace.Path, snapshot.SnapshotPath);

		Assert.True(result.Success, result.Error);
		Assert.Equal(original, File.ReadAllText(workspace.Path));
	}

	[Fact]
	public void RestoreLatest_RestoresNewestSnapshotByName()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();
		service.CreateSnapshot(workspace.Path, FirstTimestamp);
		File.WriteAllText(workspace.Path, Config("codex"));
		var newest = File.ReadAllText(workspace.Path);
		service.CreateSnapshot(workspace.Path, SecondTimestamp);
		File.WriteAllText(workspace.Path, Config("gemini"));

		var result = service.RestoreLatest(workspace.Path);

		Assert.True(result.Success, result.Error);
		Assert.Equal(newest, File.ReadAllText(workspace.Path));
		Assert.EndsWith("20260522T2037000000000Z.json", result.SnapshotPath, StringComparison.Ordinal);
	}

	[Fact]
	public void RestoreSnapshot_CorruptSnapshotReturnsFailureAndLeavesConfigUnchanged()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();
		var original = File.ReadAllText(workspace.Path);
		var corruptPath = Path.Combine(service.SnapshotDirectory(workspace.Path), "appsettings.aiunit.20260522T2038000000000Z.json");
		Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
		File.WriteAllText(corruptPath, "{ not-json");

		var result = service.RestoreSnapshot(workspace.Path, corruptPath);

		Assert.False(result.Success);
		Assert.Contains("not valid JSON", result.Error);
		Assert.Equal(original, File.ReadAllText(workspace.Path));
	}

	[Fact]
	public void ListSnapshots_ReturnsNewestFirst()
	{
		using var workspace = TempConfig.Create(Config("claude"));
		var service = new AiUnitConfigSnapshotService();
		var first = service.CreateSnapshot(workspace.Path, FirstTimestamp);
		var second = service.CreateSnapshot(workspace.Path, SecondTimestamp);

		var snapshots = service.ListSnapshots(workspace.Path);

		Assert.Equal(second.SnapshotPath, snapshots[0]);
		Assert.Equal(first.SnapshotPath, snapshots[1]);
	}

	private static string Config(string activeStrategy) =>
		$$"""
		{
		  "AiUnit": {
		    "ActiveStrategy": "{{activeStrategy}}",
		    "Strategies": {
		      "{{activeStrategy}}": {
		        "Kind": "cli",
		        "Command": "{{activeStrategy}}"
		      }
		    }
		  }
		}
		""";

	private static string Sha256(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

	private sealed class TempConfig : IDisposable
	{
		private TempConfig(string root, string path)
		{
			Root = root;
			Path = path;
		}

		public string Root { get; }

		public string Path { get; }

		public static TempConfig Create(string content)
		{
			var root = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				"aiunit-config-snapshot-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			var path = System.IO.Path.Combine(root, "appsettings.aiunit.json");
			File.WriteAllText(path, content);
			return new TempConfig(root, path);
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}
}
