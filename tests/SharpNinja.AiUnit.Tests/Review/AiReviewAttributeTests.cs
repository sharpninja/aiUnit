using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Frontier;
using SharpNinja.AiUnit.Review;
using Xunit;
using Xunit.Sdk;

namespace SharpNinja.AiUnit.Tests.Review;

public sealed class AiReviewAttributeTests
{
	[Fact]
	public void ReviewFindingsSchema_IsValidJsonAndRequiresFindings()
	{
		using var doc = JsonDocument.Parse(AiReviewFindingsSchema.JsonSchema);
		var root = doc.RootElement;

		Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
		Assert.Equal(AiReviewFindingsSchema.SchemaVersion, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetString());
		Assert.Contains(root.GetProperty("required").EnumerateArray(), x => x.GetString() == "findings");
		Assert.True(root.GetProperty("properties").TryGetProperty("agentReviews", out _));
	}

	[Fact]
	public void DefaultReviewPromptFiles_AreSpecifiedForEachReviewKind()
	{
		Assert.Equal("Review/Prompts/code-review.yaml", AiReviewPrompts.DefaultPromptFileName(AiReviewKind.Code));
		Assert.Equal("Review/Prompts/plan-review.yaml", AiReviewPrompts.DefaultPromptFileName(AiReviewKind.Plan));
		Assert.Equal("Review/Prompts/project-review.yaml", AiReviewPrompts.DefaultPromptFileName(AiReviewKind.Project));
	}

	[Fact]
	public void DefaultReviewPromptFiles_AreEmbeddedYamlResources()
	{
		var resources = typeof(AiReviewPrompts).Assembly.GetManifestResourceNames();

		Assert.Contains(AiReviewPrompts.DefaultPromptResourceName(AiReviewKind.Code), resources);
		Assert.Contains(AiReviewPrompts.DefaultPromptResourceName(AiReviewKind.Plan), resources);
		Assert.Contains(AiReviewPrompts.DefaultPromptResourceName(AiReviewKind.Project), resources);
	}

	[Fact]
	public void PromptYamlParser_ReadsLiteralPromptBlock()
	{
		var prompt = AiReviewPrompts.ParsePromptYaml(
			"""
			id: test
			prompt: |
			  First line.
			  Second line.
			""",
			"test.yaml");

		Assert.Equal($"First line.{Environment.NewLine}Second line.", prompt);
	}

	[Fact]
	public void CodeReview_EmptyPrompt_UsesYamlDefaultPrompt()
	{
		var attr = new CodeReviewAttribute("");

		var request = attr.CreateExecutionRequest();

		Assert.Equal(AiReviewKind.Code, request.ReviewKind);
		Assert.Equal(AiReviewPrompts.CannedPrompt(AiReviewKind.Code), request.Prompt);
		Assert.Contains("code review", request.Prompt, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("prompt:", request.Prompt, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void DefaultReviewPrompts_ArePrepopulatedByScope()
	{
		var code = AiReviewPrompts.CannedPrompt(AiReviewKind.Code);
		var plan = AiReviewPrompts.CannedPrompt(AiReviewKind.Plan);
		var project = AiReviewPrompts.CannedPrompt(AiReviewKind.Project);

		Assert.Contains("Scope: code review.", code, StringComparison.Ordinal);
		Assert.Contains("behavioral regressions", code, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("reviewedScope to \"code\"", code, StringComparison.Ordinal);

		Assert.Contains("Scope: plan review.", plan, StringComparison.Ordinal);
		Assert.Contains("validation gates", plan, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("reviewedScope to \"plan\"", plan, StringComparison.Ordinal);

		Assert.Contains("Scope: project review.", project, StringComparison.Ordinal);
		Assert.Contains("requirements coverage", project, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("reviewedScope to \"project\"", project, StringComparison.Ordinal);

		Assert.NotEqual(code, plan);
		Assert.NotEqual(plan, project);
		Assert.NotEqual(code, project);
	}

	[Fact]
	public void DefaultReviewPrompts_IncludeReplyJsonSchema()
	{
		foreach (var kind in Enum.GetValues<AiReviewKind>())
		{
			var prompt = AiReviewPrompts.CannedPrompt(kind);

			Assert.Contains("Return only JSON matching this schema.", prompt, StringComparison.Ordinal);
			Assert.Contains("\"$schema\": \"https://json-schema.org/draft/2020-12/schema\"", prompt, StringComparison.Ordinal);
			Assert.Contains(AiReviewFindingsSchema.SchemaVersion, prompt, StringComparison.Ordinal);
			Assert.Contains("\"reviewType\": { \"type\": \"string\", \"enum\": [\"code\", \"plan\", \"project\"] }", prompt, StringComparison.Ordinal);
			Assert.DoesNotContain("{{reviewFindingsJsonSchema}}", prompt, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void PlanReview_CustomPrompt_IsPreserved()
	{
		var attr = new PlanReviewAttribute("Review the release checklist.");

		var request = attr.CreateExecutionRequest();

		Assert.Equal(AiReviewKind.Plan, request.ReviewKind);
		Assert.Equal("Review the release checklist.", request.Prompt);
	}

	[Fact]
	public void ReviewAttributes_AreStackableDataAttributes()
	{
		var usage = typeof(CodeReviewAttribute).GetCustomAttribute<AttributeUsageAttribute>();

		Assert.NotNull(usage);
		Assert.True(usage!.AllowMultiple);
		Assert.Equal(AttributeTargets.Method, usage.ValidOn);
		Assert.True(typeof(DataAttribute).IsAssignableFrom(typeof(CodeReviewAttribute)));
		Assert.True(typeof(DataAttribute).IsAssignableFrom(typeof(PlanReviewAttribute)));
		Assert.True(typeof(DataAttribute).IsAssignableFrom(typeof(ProjectReviewAttribute)));
	}

	[Fact]
	public void Attribute_CreatesTwoParameterDataRow()
	{
		var resolver = new FakeResolver();
		var attr = new CodeReviewAttribute("Review this method.");

		var row = attr.GetData(resolver);

		Assert.Equal(2, row.Length);
		Assert.Equal("Review this method.", row[0]);
		using var doc = JsonDocument.Parse(Assert.IsType<string>(row[1]));
		Assert.Equal("pass", doc.RootElement.GetProperty("status").GetString());
		Assert.Single(resolver.DefaultClient.Requests);
		Assert.True(resolver.DefaultClient.Requests[0].RequireJsonOutput);
		Assert.Equal(0, resolver.DefaultClient.Requests[0].Temperature);
		Assert.NotNull(resolver.DefaultClient.Requests[0].Tools);
		Assert.Equal(AiReviewFindingsSchema.JsonSchema, resolver.DefaultClient.Requests[0].Tools![0].JsonSchema);
	}

	[Fact]
	public async Task Executor_MultipleAgents_UsesDefaultAggregator()
	{
		var resolver = new FakeResolver();
		resolver.Clients["agent-a"] = new FakeFrontierClient("agent-a", _ => ReviewJson("agent-a", "agent a"));
		resolver.Clients["agent-b"] = new FakeFrontierClient("agent-b", _ => ReviewJson("agent-b", "agent b"));
		resolver.DefaultClient.ResponseFactory = request =>
		{
			Assert.Contains("agent-a", request.UserMessage, StringComparison.Ordinal);
			Assert.Contains("agent-b", request.UserMessage, StringComparison.Ordinal);
			return ReviewJson("default", "aggregated");
		};
		var request = new AiReviewExecutionRequest(
			AiReviewKind.Code,
			"Review the diff.",
			new[] { new AiReviewAgentSpec(Name: "agent-a"), new AiReviewAgentSpec(Name: "agent-b") });

		var result = await AiReviewExecutor.ExecuteAsync(request, resolver);

		using var doc = JsonDocument.Parse(result);
		Assert.Equal("aggregated", doc.RootElement.GetProperty("summary").GetString());
		Assert.Single(resolver.Clients["agent-a"].Requests);
		Assert.Single(resolver.Clients["agent-b"].Requests);
		Assert.Single(resolver.DefaultClient.Requests);
	}

	[Fact]
	public void Attribute_AgentDetails_CreateExecutionRequest()
	{
		var attr = new ProjectReviewAttribute("Review packaging.")
		{
			Agent = "codex",
			Kind = "cli",
			Command = "codex",
			Model = "gpt-5",
			TimeoutSeconds = 60,
			Temperature = 0.1,
			MaxTokens = 2048,
		};

		var request = attr.CreateExecutionRequest();

		Assert.Equal(AiReviewKind.Project, request.ReviewKind);
		Assert.Equal(2048, request.MaxTokens);
		var agent = Assert.Single(request.Agents);
		Assert.Equal("codex", agent.Name);
		Assert.Equal("cli", agent.Kind);
		Assert.Equal("codex", agent.Command);
		Assert.Equal("gpt-5", agent.Model);
		Assert.Equal(60, agent.TimeoutSeconds);
		Assert.Equal(0.1, agent.Temperature);
	}

	private static string ReviewJson(string agent, string summary = "ok") =>
		$$"""
		{
		  "schemaVersion": "{{AiReviewFindingsSchema.SchemaVersion}}",
		  "reviewType": "code",
		  "status": "pass",
		  "summary": "{{summary}}",
		  "agent": { "name": "{{agent}}" },
		  "findings": []
		}
		""";

	private sealed class FakeResolver : IAiReviewClientResolver
	{
		public FakeFrontierClient DefaultClient { get; } = new("default", _ => ReviewJson("default"));

		public Dictionary<string, FakeFrontierClient> Clients { get; } = new(StringComparer.OrdinalIgnoreCase);

		public AiReviewResolvedClient ResolveDefault() => new("default", DefaultClient, null);

		public AiReviewResolvedClient Resolve(AiReviewAgentSpec spec)
		{
			var name = string.IsNullOrWhiteSpace(spec.Name) ? "inline" : spec.Name!;
			return Clients.TryGetValue(name, out var client)
				? new AiReviewResolvedClient(name, client, null)
				: new AiReviewResolvedClient(name, null, $"Missing {name}");
		}
	}

	private sealed class FakeFrontierClient : IFrontierModelClient
	{
		public FakeFrontierClient(string provider, Func<FrontierRequest, string> responseFactory)
		{
			Provider = provider;
			ResponseFactory = responseFactory;
		}

		public string Provider { get; }

		public string ModelVersion => "fake-model";

		public Func<FrontierRequest, string> ResponseFactory { get; set; }

		public List<FrontierRequest> Requests { get; } = [];

		public Task<FrontierResponse> SendAsync(
			FrontierRequest request,
			CancellationToken cancellationToken = default)
		{
			_ = cancellationToken;
			Requests.Add(request);
			return Task.FromResult(new FrontierResponse(
				ResponseFactory(request),
				FrontierTokenUsage.Zero,
				LatencyMs: 1,
				Provider,
				ModelVersion,
				EstimatedCostUsd: null,
				Error: null));
		}
	}
}
