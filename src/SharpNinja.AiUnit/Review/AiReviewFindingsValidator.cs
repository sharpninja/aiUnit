using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SharpNinja.AiUnit.Review;

internal static class AiReviewFindingsValidator
{
	private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
	{
		"schemaVersion",
		"reviewType",
		"status",
		"summary",
		"reviewedScope",
		"agent",
		"findings",
		"agentReviews",
		"runLog",
	};

	private static readonly HashSet<string> ReviewTypes = new(StringComparer.Ordinal)
	{
		"code",
		"plan",
		"project",
	};

	private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
	{
		"pass",
		"fail",
		"error",
	};

	private static readonly HashSet<string> AgentProperties = new(StringComparer.Ordinal)
	{
		"name",
		"provider",
		"model",
	};

	private static readonly HashSet<string> FindingProperties = new(StringComparer.Ordinal)
	{
		"severity",
		"category",
		"title",
		"detail",
		"recommendation",
		"filePath",
		"line",
		"ruleId",
		"confidence",
		"agent",
	};

	private static readonly HashSet<string> Severities = new(StringComparer.Ordinal)
	{
		"critical",
		"high",
		"medium",
		"low",
		"info",
	};

	private static readonly HashSet<string> AgentReviewProperties = new(StringComparer.Ordinal)
	{
		"agent",
		"result",
	};

	private static readonly HashSet<string> RunLogProperties = new(StringComparer.Ordinal)
	{
		"path",
		"url",
		"startedUtc",
	};

	public static bool TryValidate(string? text, out string error)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			error = "Response is empty.";
			return false;
		}

		try
		{
			using var doc = JsonDocument.Parse(text);
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				error = "Response root must be a JSON object.";
				return false;
			}

			return ValidateRoot(doc.RootElement, out error);
		}
		catch (JsonException ex)
		{
			error = $"Response is not valid JSON: {ex.Message}";
			return false;
		}
	}

	private static bool ValidateRoot(JsonElement root, out string error)
	{
		if (!ContainsOnly(root, RootProperties, "$", out error))
		{
			return false;
		}

		if (!RequireString(root, "schemaVersion", "$", out var schemaVersion, out error))
		{
			return false;
		}

		if (!string.Equals(schemaVersion, AiReviewFindingsSchema.SchemaVersion, StringComparison.Ordinal))
		{
			error = $"$.schemaVersion must be '{AiReviewFindingsSchema.SchemaVersion}'.";
			return false;
		}

		if (!RequireString(root, "reviewType", "$", out var reviewType, out error))
		{
			return false;
		}

		if (!ReviewTypes.Contains(reviewType))
		{
			error = "$.reviewType must be one of: code, plan, project.";
			return false;
		}

		if (!RequireString(root, "status", "$", out var status, out error))
		{
			return false;
		}

		if (!Statuses.Contains(status))
		{
			error = "$.status must be one of: pass, fail, error.";
			return false;
		}

		if (!RequireString(root, "summary", "$", out _, out error))
		{
			return false;
		}

		if (root.TryGetProperty("reviewedScope", out var reviewedScope) &&
			!RequireStringValue(reviewedScope, "$.reviewedScope", out error))
		{
			return false;
		}

		if (root.TryGetProperty("agent", out var agent) &&
			!ValidateAgent(agent, "$.agent", out error))
		{
			return false;
		}

		if (!root.TryGetProperty("findings", out var findings))
		{
			error = "$ is missing required property 'findings'.";
			return false;
		}

		if (!ValidateFindings(findings, out error))
		{
			return false;
		}

		if (root.TryGetProperty("agentReviews", out var agentReviews) &&
			!ValidateAgentReviews(agentReviews, out error))
		{
			return false;
		}

		if (root.TryGetProperty("runLog", out var runLog) &&
			!ValidateRunLog(runLog, out error))
		{
			return false;
		}

		error = string.Empty;
		return true;
	}

	private static bool ValidateAgent(JsonElement agent, string path, out string error)
	{
		if (agent.ValueKind != JsonValueKind.Object)
		{
			error = $"{path} must be an object.";
			return false;
		}

		if (!ContainsOnly(agent, AgentProperties, path, out error))
		{
			return false;
		}

		if (!RequireString(agent, "name", path, out _, out error))
		{
			return false;
		}

		return OptionalString(agent, "provider", path, out error) &&
			OptionalString(agent, "model", path, out error);
	}

	private static bool ValidateFindings(JsonElement findings, out string error)
	{
		if (findings.ValueKind != JsonValueKind.Array)
		{
			error = "$.findings must be an array.";
			return false;
		}

		var index = 0;
		foreach (var finding in findings.EnumerateArray())
		{
			if (!ValidateFinding(finding, $"$.findings[{index}]", out error))
			{
				return false;
			}
			index++;
		}

		error = string.Empty;
		return true;
	}

	private static bool ValidateFinding(JsonElement finding, string path, out string error)
	{
		if (finding.ValueKind != JsonValueKind.Object)
		{
			error = $"{path} must be an object.";
			return false;
		}

		if (!ContainsOnly(finding, FindingProperties, path, out error))
		{
			return false;
		}

		if (!RequireString(finding, "severity", path, out var severity, out error))
		{
			return false;
		}

		if (!Severities.Contains(severity))
		{
			error = $"{path}.severity must be one of: critical, high, medium, low, info.";
			return false;
		}

		if (!RequireString(finding, "title", path, out _, out error) ||
			!RequireString(finding, "detail", path, out _, out error) ||
			!RequireString(finding, "recommendation", path, out _, out error))
		{
			return false;
		}

		if (!OptionalString(finding, "category", path, out error) ||
			!OptionalString(finding, "filePath", path, out error) ||
			!OptionalString(finding, "ruleId", path, out error) ||
			!OptionalString(finding, "agent", path, out error))
		{
			return false;
		}

		if (finding.TryGetProperty("line", out var line) &&
			(line.ValueKind != JsonValueKind.Number || !line.TryGetInt32(out var lineNumber) || lineNumber < 1))
		{
			error = $"{path}.line must be an integer greater than or equal to 1.";
			return false;
		}

		if (finding.TryGetProperty("confidence", out var confidence) &&
			(confidence.ValueKind != JsonValueKind.Number ||
			 !confidence.TryGetDouble(out var confidenceValue) ||
			 confidenceValue < 0 ||
			 confidenceValue > 1))
		{
			error = $"{path}.confidence must be a number between 0 and 1.";
			return false;
		}

		error = string.Empty;
		return true;
	}

	private static bool ValidateAgentReviews(JsonElement agentReviews, out string error)
	{
		if (agentReviews.ValueKind != JsonValueKind.Array)
		{
			error = "$.agentReviews must be an array.";
			return false;
		}

		var index = 0;
		foreach (var agentReview in agentReviews.EnumerateArray())
		{
			var path = $"$.agentReviews[{index}]";
			if (agentReview.ValueKind != JsonValueKind.Object)
			{
				error = $"{path} must be an object.";
				return false;
			}

			if (!ContainsOnly(agentReview, AgentReviewProperties, path, out error) ||
				!RequireString(agentReview, "agent", path, out _, out error))
			{
				return false;
			}

			if (!agentReview.TryGetProperty("result", out var result))
			{
				error = $"{path} is missing required property 'result'.";
				return false;
			}

			if (result.ValueKind != JsonValueKind.Object)
			{
				error = $"{path}.result must be an object.";
				return false;
			}

			index++;
		}

		error = string.Empty;
		return true;
	}

	private static bool ValidateRunLog(JsonElement runLog, out string error)
	{
		if (runLog.ValueKind != JsonValueKind.Object)
		{
			error = "$.runLog must be an object.";
			return false;
		}

		if (!ContainsOnly(runLog, RunLogProperties, "$.runLog", out error) ||
			!RequireString(runLog, "path", "$.runLog", out _, out error))
		{
			return false;
		}

		return OptionalString(runLog, "url", "$.runLog", out error) &&
			OptionalString(runLog, "startedUtc", "$.runLog", out error);
	}

	private static bool ContainsOnly(JsonElement element, HashSet<string> allowedProperties, string path, out string error)
	{
		foreach (var property in element.EnumerateObject())
		{
			if (!allowedProperties.Contains(property.Name))
			{
				error = $"{path} contains unsupported property '{property.Name}'.";
				return false;
			}
		}

		error = string.Empty;
		return true;
	}

	private static bool RequireString(
		JsonElement element,
		string name,
		string path,
		out string value,
		out string error)
	{
		if (!element.TryGetProperty(name, out var property))
		{
			value = string.Empty;
			error = $"{path} is missing required property '{name}'.";
			return false;
		}

		if (property.ValueKind != JsonValueKind.String)
		{
			value = string.Empty;
			error = $"{path}.{name} must be a string.";
			return false;
		}

		value = property.GetString() ?? string.Empty;
		error = string.Empty;
		return true;
	}

	private static bool OptionalString(JsonElement element, string name, string path, out string error)
	{
		if (!element.TryGetProperty(name, out var property))
		{
			error = string.Empty;
			return true;
		}

		return RequireStringValue(property, $"{path}.{name}", out error);
	}

	private static bool RequireStringValue(JsonElement element, string path, out string error)
	{
		if (element.ValueKind != JsonValueKind.String)
		{
			error = $"{path} must be a string.";
			return false;
		}

		error = string.Empty;
		return true;
	}
}
