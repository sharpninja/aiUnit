using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace SharpNinja.AiUnit.Review;

/// <summary>
/// Base xUnit data attribute for aiUnit review test rows. The generated data
/// row contains the effective prompt as the first parameter and the review
/// result JSON as the second parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public abstract class AiReviewAttribute : DataAttribute
{
	private readonly AiReviewKind _reviewKind;
	private readonly string? _prompt;

	/// <summary>Creates a review attribute for a built-in review kind.</summary>
	protected AiReviewAttribute(AiReviewKind reviewKind, string? prompt)
	{
		_reviewKind = reviewKind;
		_prompt = prompt;
	}

	/// <summary>Single configured agent strategy name.</summary>
	public string? Agent { get; set; }

	/// <summary>Multiple configured agent strategy names. When more than one agent is supplied, the default agent aggregates the results.</summary>
	public string[]? Agents { get; set; }

	/// <summary>Inline agent kind: anthropic, openai-compatible, gemini, or cli.</summary>
	public string? Kind { get; set; }

	/// <summary>Inline HTTP base URL for HTTP agents.</summary>
	public string? BaseUrl { get; set; }

	/// <summary>Inline model name.</summary>
	public string? Model { get; set; }

	/// <summary>Name of the environment variable containing the API key.</summary>
	public string? ApiKeyEnvVar { get; set; }

	/// <summary>CLI command for cli agents.</summary>
	public string? Command { get; set; }

	/// <summary>Per-call timeout in seconds. Values less than one use the configured strategy default.</summary>
	public int TimeoutSeconds { get; set; }

	/// <summary>Sampling temperature. Defaults to the configured strategy value.</summary>
	public double Temperature { get; set; } = double.NaN;

	/// <summary>Optional max-token budget for the review response.</summary>
	public int MaxTokens { get; set; }

	/// <inheritdoc />
	public override IEnumerable<object[]> GetData(MethodInfo testMethod)
	{
		_ = testMethod;
		yield return GetData(new DefaultAiReviewClientResolver());
	}

	internal object[] GetData(IAiReviewClientResolver resolver)
	{
		var request = CreateExecutionRequest();
		var resultJson = AiReviewExecutor
			.ExecuteAsync(request, resolver)
			.GetAwaiter()
			.GetResult();
		return [request.Prompt, resultJson];
	}

	internal AiReviewExecutionRequest CreateExecutionRequest()
	{
		var prompt = AiReviewPrompts.EffectivePrompt(_reviewKind, _prompt);
		return new AiReviewExecutionRequest(
			_reviewKind,
			prompt,
			BuildAgentSpecs(),
			MaxTokens is > 0 ? MaxTokens : null);
	}

	private IReadOnlyList<AiReviewAgentSpec> BuildAgentSpecs()
	{
		var specs = new List<AiReviewAgentSpec>();
		if (Agents is { Length: > 0 })
		{
			specs.AddRange(Agents
				.Where(static x => !string.IsNullOrWhiteSpace(x))
				.Select(x => new AiReviewAgentSpec(Name: x.Trim())));
		}
		else if (!string.IsNullOrWhiteSpace(Agent))
		{
			specs.Add(new AiReviewAgentSpec(Name: Agent.Trim()));
		}

		if (HasInlineDetails())
		{
			if (specs.Count == 1)
			{
				var current = specs[0];
				specs[0] = current with
				{
					Kind = Kind,
					BaseUrl = BaseUrl,
					Model = Model,
					ApiKeyEnvVar = ApiKeyEnvVar,
					Command = Command,
					TimeoutSeconds = TimeoutSeconds is > 0 ? TimeoutSeconds : null,
					Temperature = double.IsNaN(Temperature) ? null : Temperature,
				};
			}
			else
			{
				specs.Add(new AiReviewAgentSpec(
					Name: Agent,
					Kind: Kind,
					BaseUrl: BaseUrl,
					Model: Model,
					ApiKeyEnvVar: ApiKeyEnvVar,
					Command: Command,
					TimeoutSeconds: TimeoutSeconds is > 0 ? TimeoutSeconds : null,
					Temperature: double.IsNaN(Temperature) ? null : Temperature));
			}
		}

		return specs;
	}

	private bool HasInlineDetails() =>
		!string.IsNullOrWhiteSpace(Kind)
		|| !string.IsNullOrWhiteSpace(BaseUrl)
		|| !string.IsNullOrWhiteSpace(Model)
		|| !string.IsNullOrWhiteSpace(ApiKeyEnvVar)
		|| !string.IsNullOrWhiteSpace(Command)
		|| TimeoutSeconds is > 0
		|| !double.IsNaN(Temperature);
}
