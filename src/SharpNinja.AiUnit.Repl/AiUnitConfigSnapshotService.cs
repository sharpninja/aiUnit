using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpNinja.AiUnit.Repl;

public sealed record AiUnitConfigSnapshot(
	string ConfigPath,
	string SnapshotPath,
	DateTimeOffset CreatedAtUtc,
	long Bytes,
	string Sha256);

public sealed record AiUnitConfigRestoreResult(
	bool Success,
	string? Error,
	string? SnapshotPath);

public sealed class AiUnitConfigSnapshotService
{
	private const string SnapshotDirectoryName = ".aiunit";
	private const string SnapshotSubdirectoryName = "snapshots";

	public string SnapshotDirectory(string configPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

		var directory = Path.GetDirectoryName(Path.GetFullPath(configPath))
			?? Directory.GetCurrentDirectory();
		return Path.Combine(directory, SnapshotDirectoryName, SnapshotSubdirectoryName);
	}

	public AiUnitConfigSnapshot CreateSnapshot(
		string configPath,
		DateTimeOffset? createdAtUtc = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

		var fullConfigPath = Path.GetFullPath(configPath);
		if (!File.Exists(fullConfigPath))
		{
			throw new FileNotFoundException("aiUnit config file was not found.", fullConfigPath);
		}

		var timestamp = (createdAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
		var snapshotDirectory = SnapshotDirectory(fullConfigPath);
		Directory.CreateDirectory(snapshotDirectory);

		var sourceBytes = File.ReadAllBytes(fullConfigPath);
		var snapshotPath = UniqueSnapshotPath(fullConfigPath, timestamp);
		var tempPath = snapshotPath + ".tmp";

		File.WriteAllBytes(tempPath, sourceBytes);
		var copiedBytes = File.ReadAllBytes(tempPath);
		if (!sourceBytes.SequenceEqual(copiedBytes))
		{
			File.Delete(tempPath);
			throw new IOException("Snapshot verification failed; copied bytes differ from source config.");
		}

		File.Move(tempPath, snapshotPath);

		return new AiUnitConfigSnapshot(
			fullConfigPath,
			snapshotPath,
			timestamp,
			sourceBytes.LongLength,
			Sha256(sourceBytes));
	}

	public IReadOnlyList<string> ListSnapshots(string configPath)
	{
		var snapshotDirectory = SnapshotDirectory(configPath);
		if (!Directory.Exists(snapshotDirectory))
		{
			return Array.Empty<string>();
		}

		var fileName = Path.GetFileNameWithoutExtension(configPath);
		return Directory.EnumerateFiles(snapshotDirectory, $"{fileName}.*.json", SearchOption.TopDirectoryOnly)
			.OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public AiUnitConfigRestoreResult RestoreLatest(string configPath)
	{
		var snapshotPath = ListSnapshots(configPath).FirstOrDefault();
		return snapshotPath is null
			? new AiUnitConfigRestoreResult(false, "No snapshots were found.", null)
			: RestoreSnapshot(configPath, snapshotPath);
	}

	public AiUnitConfigRestoreResult RestoreSnapshot(string configPath, string snapshotPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);

		var fullConfigPath = Path.GetFullPath(configPath);
		var fullSnapshotPath = Path.GetFullPath(snapshotPath);
		if (!File.Exists(fullSnapshotPath))
		{
			return new AiUnitConfigRestoreResult(false, "Snapshot file was not found.", fullSnapshotPath);
		}

		var snapshotBytes = File.ReadAllBytes(fullSnapshotPath);
		if (!IsValidJson(snapshotBytes, out var error))
		{
			return new AiUnitConfigRestoreResult(false, $"Snapshot is not valid JSON: {error}", fullSnapshotPath);
		}

		Directory.CreateDirectory(Path.GetDirectoryName(fullConfigPath)!);
		File.WriteAllBytes(fullConfigPath, snapshotBytes);
		var restoredBytes = File.ReadAllBytes(fullConfigPath);
		if (!snapshotBytes.SequenceEqual(restoredBytes))
		{
			return new AiUnitConfigRestoreResult(false, "Restore verification failed; restored bytes differ from snapshot.", fullSnapshotPath);
		}

		return new AiUnitConfigRestoreResult(true, null, fullSnapshotPath);
	}

	private string UniqueSnapshotPath(string configPath, DateTimeOffset timestamp)
	{
		var snapshotDirectory = SnapshotDirectory(configPath);
		var fileName = Path.GetFileNameWithoutExtension(configPath);
		var stamp = timestamp.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture);
		var candidate = Path.Combine(snapshotDirectory, $"{fileName}.{stamp}.json");
		if (!File.Exists(candidate))
		{
			return candidate;
		}

		for (var suffix = 1; suffix < int.MaxValue; suffix++)
		{
			candidate = Path.Combine(snapshotDirectory, $"{fileName}.{stamp}-{suffix}.json");
			if (!File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new IOException("Could not allocate a unique snapshot path.");
	}

	private static bool IsValidJson(byte[] bytes, out string error)
	{
		try
		{
			using var _ = JsonDocument.Parse(bytes);
			error = string.Empty;
			return true;
		}
		catch (JsonException ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static string Sha256(byte[] bytes) =>
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
