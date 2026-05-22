namespace SharpNinja.AiUnit.Review;

/// <summary>JSON schema used by review agents when reporting findings.</summary>
public static class AiReviewFindingsSchema
{
	/// <summary>Schema identifier emitted by compliant review results.</summary>
	public const string SchemaVersion = "aiunit.review.findings.v1";

	/// <summary>Draft 2020-12 JSON schema for aiUnit review findings.</summary>
	public const string JsonSchema = """
	{
	  "$schema": "https://json-schema.org/draft/2020-12/schema",
	  "$id": "https://sharpninja.dev/schemas/aiunit-review-findings-v1.json",
	  "title": "aiUnit Review Findings",
	  "type": "object",
	  "additionalProperties": false,
	  "required": ["schemaVersion", "reviewType", "status", "summary", "findings"],
	  "properties": {
	    "schemaVersion": { "const": "aiunit.review.findings.v1" },
	    "reviewType": { "type": "string", "enum": ["code", "plan", "project"] },
	    "status": { "type": "string", "enum": ["pass", "fail", "error"] },
	    "summary": { "type": "string" },
	    "reviewedScope": { "type": "string" },
	    "agent": {
	      "type": "object",
	      "additionalProperties": false,
	      "required": ["name"],
	      "properties": {
	        "name": { "type": "string" },
	        "provider": { "type": "string" },
	        "model": { "type": "string" }
	      }
	    },
	    "findings": {
	      "type": "array",
	      "items": {
	        "type": "object",
	        "additionalProperties": false,
	        "required": ["severity", "title", "detail", "recommendation"],
	        "properties": {
	          "severity": { "type": "string", "enum": ["critical", "high", "medium", "low", "info"] },
	          "category": { "type": "string" },
	          "title": { "type": "string" },
	          "detail": { "type": "string" },
	          "recommendation": { "type": "string" },
	          "filePath": { "type": "string" },
	          "line": { "type": "integer", "minimum": 1 },
	          "ruleId": { "type": "string" },
	          "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
	          "agent": { "type": "string" }
	        }
	      }
	    },
	    "agentReviews": {
	      "type": "array",
	      "items": {
	        "type": "object",
	        "additionalProperties": false,
	        "required": ["agent", "result"],
	        "properties": {
	          "agent": { "type": "string" },
	          "result": { "type": "object" }
	        }
	      }
	    }
	  }
	}
	""";
}
