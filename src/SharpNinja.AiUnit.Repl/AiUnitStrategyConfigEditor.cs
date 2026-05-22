using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpNinja.AiUnit.Repl;

public sealed record AiUnitStrategyDefinition(
	string Kind,
	string? BaseUrl = null,
	string? Model = null,
	string? ApiKeyEnvVar = null,
	string? Command = null,
	int? TimeoutSeconds = null,
	double? Temperature = null,
	string? Description = null);

public sealed class AiUnitStrategyConfigEditor
{
	private static readonly JsonSerializerOptions WriteOptions = new()
	{
		WriteIndented = true,
	};

	private readonly JsonObject _root;
	private readonly JsonObject _aiUnit;
	private readonly JsonObject _strategies;

	private AiUnitStrategyConfigEditor(
		string path,
		JsonObject root,
		JsonObject aiUnit,
		JsonObject strategies)
	{
		Path = path;
		_root = root;
		_aiUnit = aiUnit;
		_strategies = strategies;
	}

	public string Path { get; }

	public string? ActiveStrategy => _aiUnit["ActiveStrategy"]?.GetValue<string>();

	public IReadOnlyList<string> StrategyNames =>
		_strategies.Select(pair => pair.Key).ToArray();

	public bool TryGetStrategyDefinition(
		string strategyName,
		out AiUnitStrategyDefinition? definition)
	{
		var key = FindStrategyKey(strategyName);
		if (key is null || _strategies[key] is not JsonObject strategy)
		{
			definition = null;
			return false;
		}

		definition = ToDefinition(strategy);
		return true;
	}

	public static AiUnitStrategyConfigEditor Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		var root = File.Exists(path)
			? JsonNode.Parse(
				File.ReadAllText(path),
				documentOptions: new JsonDocumentOptions
				{
					AllowTrailingCommas = true,
					CommentHandling = JsonCommentHandling.Skip,
				}) as JsonObject
			: new JsonObject();

		root ??= new JsonObject();
		var aiUnit = EnsureObject(root, "AiUnit");
		var strategies = EnsureObject(aiUnit, "Strategies");

		return new AiUnitStrategyConfigEditor(
			System.IO.Path.GetFullPath(path),
			root,
			aiUnit,
			strategies);
	}

	public bool TrySetActiveStrategy(string strategyName, out string error)
	{
		if (string.IsNullOrWhiteSpace(strategyName))
		{
			error = "Strategy name is required.";
			return false;
		}

		var existingKey = FindStrategyKey(strategyName);
		if (existingKey is null)
		{
			error = $"Strategy '{strategyName}' is not configured.";
			return false;
		}

		_aiUnit["ActiveStrategy"] = existingKey;
		error = string.Empty;
		return true;
	}

	public bool TryAddOrUpdateStrategy(
		string strategyName,
		AiUnitStrategyDefinition definition,
		out string error)
	{
		if (string.IsNullOrWhiteSpace(strategyName))
		{
			error = "Strategy name is required.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(definition.Kind))
		{
			error = "Strategy Kind is required.";
			return false;
		}

		var key = FindStrategyKey(strategyName) ?? strategyName;
		_strategies[key] = ToJsonObject(definition);
		error = string.Empty;
		return true;
	}

	public bool TryRemoveStrategy(string strategyName, out string error)
	{
		if (string.IsNullOrWhiteSpace(strategyName))
		{
			error = "Strategy name is required.";
			return false;
		}

		var existingKey = FindStrategyKey(strategyName);
		if (existingKey is null)
		{
			error = $"Strategy '{strategyName}' is not configured.";
			return false;
		}

		if (string.Equals(ActiveStrategy, existingKey, StringComparison.OrdinalIgnoreCase))
		{
			error = $"Cannot remove active strategy '{existingKey}'.";
			return false;
		}

		_strategies.Remove(existingKey);
		error = string.Empty;
		return true;
	}

	public IReadOnlyList<string> Validate()
	{
		var messages = new List<string>();
		var names = StrategyNames;
		var activeStrategy = ActiveStrategy;

		if (string.IsNullOrWhiteSpace(activeStrategy))
		{
			messages.Add("ActiveStrategy is not configured.");
		}
		else if (!names.Contains(activeStrategy, StringComparer.OrdinalIgnoreCase))
		{
			messages.Add($"Active strategy '{activeStrategy}' is not defined in Strategies.");
		}

		if (names.Count == 0)
		{
			messages.Add("No strategies are configured.");
		}

		foreach (var pair in _strategies)
		{
			if (pair.Value is not JsonObject strategy)
			{
				messages.Add($"Strategy '{pair.Key}' must be a JSON object.");
				continue;
			}

			if (!strategy.TryGetPropertyValue("Kind", out var kind)
				|| kind is null
				|| kind.GetValueKind() != JsonValueKind.String
				|| string.IsNullOrWhiteSpace(kind.GetValue<string>()))
			{
				messages.Add($"Strategy '{pair.Key}' is missing required Kind.");
			}
		}

		return messages;
	}

	public string ToJson() => _root.ToJsonString(WriteOptions) + Environment.NewLine;

	public void Save()
	{
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
		File.WriteAllText(Path, ToJson());
	}

	private string? FindStrategyKey(string strategyName) =>
		_strategies.Select(pair => pair.Key)
			.FirstOrDefault(key => string.Equals(key, strategyName, StringComparison.OrdinalIgnoreCase));

	private static JsonObject EnsureObject(JsonObject parent, string propertyName)
	{
		if (parent[propertyName] is JsonObject existing)
		{
			return existing;
		}

		var created = new JsonObject();
		parent[propertyName] = created;
		return created;
	}

	private static JsonObject ToJsonObject(AiUnitStrategyDefinition definition)
	{
		var obj = new JsonObject
		{
			["Kind"] = definition.Kind,
		};

		AddIfNotNull(obj, "BaseUrl", definition.BaseUrl);
		AddIfNotNull(obj, "Model", definition.Model);
		AddIfNotNull(obj, "ApiKeyEnvVar", definition.ApiKeyEnvVar);
		AddIfNotNull(obj, "Command", definition.Command);
		AddIfNotNull(obj, "TimeoutSeconds", definition.TimeoutSeconds);
		AddIfNotNull(obj, "Temperature", definition.Temperature);
		AddIfNotNull(obj, "Description", definition.Description);

		return obj;
	}

	private static AiUnitStrategyDefinition ToDefinition(JsonObject obj) =>
		new(
			Kind: StringValue(obj, "Kind") ?? string.Empty,
			BaseUrl: StringValue(obj, "BaseUrl"),
			Model: StringValue(obj, "Model"),
			ApiKeyEnvVar: StringValue(obj, "ApiKeyEnvVar"),
			Command: StringValue(obj, "Command"),
			TimeoutSeconds: IntValue(obj, "TimeoutSeconds"),
			Temperature: DoubleValue(obj, "Temperature"),
			Description: StringValue(obj, "Description"));

	private static string? StringValue(JsonObject obj, string propertyName) =>
		obj.TryGetPropertyValue(propertyName, out var value)
			&& value is not null
			&& value.GetValueKind() == JsonValueKind.String
				? value.GetValue<string>()
				: null;

	private static int? IntValue(JsonObject obj, string propertyName) =>
		obj.TryGetPropertyValue(propertyName, out var value)
			&& value is not null
			&& value.GetValueKind() == JsonValueKind.Number
				? value.GetValue<int>()
				: null;

	private static double? DoubleValue(JsonObject obj, string propertyName) =>
		obj.TryGetPropertyValue(propertyName, out var value)
			&& value is not null
			&& value.GetValueKind() == JsonValueKind.Number
				? value.GetValue<double>()
				: null;

	private static void AddIfNotNull(JsonObject obj, string propertyName, string? value)
	{
		if (value is not null)
		{
			obj[propertyName] = value;
		}
	}

	private static void AddIfNotNull(JsonObject obj, string propertyName, int? value)
	{
		if (value is not null)
		{
			obj[propertyName] = value.Value;
		}
	}

	private static void AddIfNotNull(JsonObject obj, string propertyName, double? value)
	{
		if (value is not null)
		{
			obj[propertyName] = value.Value;
		}
	}
}
