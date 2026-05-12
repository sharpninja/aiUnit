using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SharpNinja.AiUnit.Validation;

/// <summary>
/// Static guard helpers consumers compose into scenario validators when
/// checking the shape of a frontier model's JSON response. Each helper throws
/// <see cref="AiResponseValidationException"/> on contract violation with the
/// offending field name embedded in the message.
/// </summary>
public static class AiUnitJsonAssertions
{
	/// <summary>
	/// Asserts that every key in <paramref name="keys"/> exists as a property
	/// of the root object. Throws on the first missing key.
	/// </summary>
	/// <param name="root">Root JSON element (must be a JSON object).</param>
	/// <param name="keys">Required keys.</param>
	/// <exception cref="AiResponseValidationException">
	/// Thrown when any required key is missing.
	/// </exception>
	public static void Required(JsonElement root, params string[] keys)
	{
		if (keys is null || keys.Length == 0)
		{
			return;
		}
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new AiResponseValidationException(
				$"Required: expected a JSON object but received {root.ValueKind}.");
		}
		foreach (var key in keys)
		{
			if (!root.TryGetProperty(key, out _))
			{
				throw new AiResponseValidationException(
					$"Required key '{key}' is missing from the response object.");
			}
		}
	}

	/// <summary>
	/// Asserts that the string property named <paramref name="fieldName"/>
	/// on <paramref name="element"/> has a value in
	/// <paramref name="allowed"/>. The default comparer is case-sensitive;
	/// callers can relax this by passing
	/// <see cref="StringComparer.OrdinalIgnoreCase"/>.
	/// </summary>
	/// <param name="element">Parent object containing the property.</param>
	/// <param name="fieldName">Name of the string property to validate.</param>
	/// <param name="allowed">Permitted values.</param>
	/// <param name="comparer">Optional case-folding comparer.</param>
	public static void EnumIn(
		JsonElement element,
		string fieldName,
		IReadOnlyCollection<string> allowed,
		StringComparer? comparer = null)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new AiResponseValidationException(
				$"EnumIn: expected JSON object for '{fieldName}' parent but received {element.ValueKind}.");
		}
		if (!element.TryGetProperty(fieldName, out var prop))
		{
			throw new AiResponseValidationException(
				$"EnumIn: required field '{fieldName}' is missing.");
		}
		if (prop.ValueKind != JsonValueKind.String)
		{
			throw new AiResponseValidationException(
				$"EnumIn: '{fieldName}' expected JSON string but received {prop.ValueKind}.");
		}
		var raw = prop.GetString() ?? string.Empty;
		var cmp = comparer ?? StringComparer.Ordinal;
		if (!allowed.Contains(raw, cmp))
		{
			var allowedRendered = string.Join(", ", allowed);
			throw new AiResponseValidationException(
				$"EnumIn: '{fieldName}' value '{raw}' is not in allowed set [{allowedRendered}].");
		}
	}

	/// <summary>
	/// Asserts that the property named <paramref name="fieldName"/> is a JSON
	/// array of strings (every element <see cref="JsonValueKind.String"/>).
	/// </summary>
	/// <param name="element">Parent JSON object.</param>
	/// <param name="fieldName">Name of the array property to validate.</param>
	public static void StringArray(JsonElement element, string fieldName)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new AiResponseValidationException(
				$"StringArray: expected JSON object for '{fieldName}' parent but received {element.ValueKind}.");
		}
		if (!element.TryGetProperty(fieldName, out var prop))
		{
			throw new AiResponseValidationException(
				$"StringArray: required field '{fieldName}' is missing.");
		}
		if (prop.ValueKind != JsonValueKind.Array)
		{
			throw new AiResponseValidationException(
				$"StringArray: '{fieldName}' expected JSON array but received {prop.ValueKind}.");
		}
		var index = 0;
		foreach (var item in prop.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.String)
			{
				throw new AiResponseValidationException(
					$"StringArray: '{fieldName}[{index}]' expected JSON string but received {item.ValueKind}.");
			}
			index++;
		}
	}

	/// <summary>
	/// Asserts that the property named <paramref name="fieldName"/> is a JSON
	/// array of objects, each containing every key in
	/// <paramref name="requiredObjectKeys"/>.
	/// </summary>
	/// <param name="element">Parent JSON object.</param>
	/// <param name="fieldName">Name of the array property to validate.</param>
	/// <param name="requiredObjectKeys">Keys every object item must contain.</param>
	public static void ObjectArrayRequired(
		JsonElement element,
		string fieldName,
		params string[] requiredObjectKeys)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new AiResponseValidationException(
				$"ObjectArrayRequired: expected JSON object for '{fieldName}' parent but received {element.ValueKind}.");
		}
		if (!element.TryGetProperty(fieldName, out var prop))
		{
			throw new AiResponseValidationException(
				$"ObjectArrayRequired: required field '{fieldName}' is missing.");
		}
		if (prop.ValueKind != JsonValueKind.Array)
		{
			throw new AiResponseValidationException(
				$"ObjectArrayRequired: '{fieldName}' expected JSON array but received {prop.ValueKind}.");
		}
		var index = 0;
		foreach (var item in prop.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new AiResponseValidationException(
					$"ObjectArrayRequired: '{fieldName}[{index}]' expected JSON object but received {item.ValueKind}.");
			}
			if (requiredObjectKeys is not null)
			{
				foreach (var key in requiredObjectKeys)
				{
					if (!item.TryGetProperty(key, out _))
					{
						throw new AiResponseValidationException(
							$"ObjectArrayRequired: '{fieldName}[{index}]' is missing required key '{key}'.");
					}
				}
			}
			index++;
		}
	}
}
