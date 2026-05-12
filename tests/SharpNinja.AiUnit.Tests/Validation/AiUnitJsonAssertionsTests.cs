using System;
using System.Collections.Generic;
using System.Text.Json;
using SharpNinja.AiUnit.Validation;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Validation;

/// <summary>
/// Phase-4 validation-helper coverage: every helper throws
/// <see cref="AiResponseValidationException"/> on failure with the offending
/// field name embedded in the message, and accepts conforming input. These
/// are the building blocks consumers compose into their per-scenario
/// validators.
/// </summary>
public class AiUnitJsonAssertionsTests
{
	/// <summary>
	/// <see cref="AiUnitJsonAssertions.Required"/> must throw when any of
	/// the supplied keys is missing from the JSON root object. The message
	/// must include the missing key so consumers can diagnose quickly.
	/// </summary>
	[Fact]
	public void Required_MissingKey_Throws()
	{
		using var doc = JsonDocument.Parse("{ \"alpha\": 1 }");

		var ex = Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.Required(doc.RootElement, "alpha", "beta"));

		Assert.Contains("beta", ex.Message, StringComparison.Ordinal);
	}

	/// <summary>
	/// <see cref="AiUnitJsonAssertions.EnumIn"/> passes when the property
	/// value is in the allowed set, and throws otherwise. The default
	/// comparer is case-sensitive (consumers pass a comparer to relax).
	/// </summary>
	[Fact]
	public void EnumIn_ValidatesMembership()
	{
		using var ok = JsonDocument.Parse("{ \"severity\": \"medium\" }");
		AiUnitJsonAssertions.EnumIn(ok.RootElement, "severity",
			new[] { "low", "medium", "high" });

		using var bad = JsonDocument.Parse("{ \"severity\": \"critical\" }");
		var ex = Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.EnumIn(bad.RootElement, "severity",
				new[] { "low", "medium", "high" }));
		Assert.Contains("severity", ex.Message, StringComparison.Ordinal);
		Assert.Contains("critical", ex.Message, StringComparison.Ordinal);
	}

	/// <summary>
	/// <see cref="AiUnitJsonAssertions.StringArray"/> throws when the property
	/// is not a JSON array of strings (object / number / null / mixed-type
	/// array all fail). It passes for an array of all-string members.
	/// </summary>
	[Fact]
	public void StringArray_RejectsNonArray()
	{
		using var ok = JsonDocument.Parse("{ \"tags\": [\"a\", \"b\"] }");
		AiUnitJsonAssertions.StringArray(ok.RootElement, "tags");

		using var notArray = JsonDocument.Parse("{ \"tags\": \"a,b\" }");
		Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.StringArray(notArray.RootElement, "tags"));

		using var arrayOfNumbers = JsonDocument.Parse("{ \"tags\": [1, 2] }");
		Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.StringArray(arrayOfNumbers.RootElement, "tags"));

		using var missing = JsonDocument.Parse("{ }");
		Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.StringArray(missing.RootElement, "tags"));
	}

	/// <summary>
	/// <see cref="AiUnitJsonAssertions.ObjectArrayRequired"/> walks each item
	/// in the array verifying that EVERY required object key is present.
	/// Throws on first violation with the item index and missing key in the
	/// message.
	/// </summary>
	[Fact]
	public void ObjectArrayRequired_ValidatesPerItemKeys()
	{
		const string ok = """
		{
			"items": [
				{ "id": "a", "label": "Apple" },
				{ "id": "b", "label": "Banana" }
			]
		}
		""";
		using var okDoc = JsonDocument.Parse(ok);
		AiUnitJsonAssertions.ObjectArrayRequired(okDoc.RootElement, "items", "id", "label");

		const string missingLabel = """
		{
			"items": [
				{ "id": "a", "label": "Apple" },
				{ "id": "b" }
			]
		}
		""";
		using var badDoc = JsonDocument.Parse(missingLabel);
		var ex = Assert.Throws<AiResponseValidationException>(() =>
			AiUnitJsonAssertions.ObjectArrayRequired(badDoc.RootElement, "items", "id", "label"));
		Assert.Contains("label", ex.Message, StringComparison.Ordinal);
	}
}
