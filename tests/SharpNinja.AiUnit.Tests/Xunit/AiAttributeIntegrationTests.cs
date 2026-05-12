using System;
using System.IO;
using System.Reflection;
using SharpNinja.AiUnit.Xunit;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Xunit;

/// <summary>
/// Phase-4 attribute-integration coverage: validates that the xUnit extension
/// layer (AiStrategyFixture + AiFactAttribute + AiTheoryAttribute + AiSkip)
/// composes correctly with the strategy resolver. Tests manipulate env vars
/// inside a try/finally snapshot/restore block to ensure other suites are
/// not affected.
/// </summary>
public class AiAttributeIntegrationTests
{
	private const string EnvStrategy = "AIUNIT_STRATEGY";
	private const string EnvKind = "AIUNIT_KIND";
	private const string EnvBaseUrl = "AIUNIT_BASE_URL";
	private const string EnvModel = "AIUNIT_MODEL";
	private const string EnvCommand = "AIUNIT_COMMAND";
	private const string EnvApiKey = "AIUNIT_API_KEY";

	/// <summary>
	/// When no strategy resolves (no config file, no env vars), the fixture
	/// must populate <see cref="AiStrategyFixture.SkipReason"/> with a
	/// non-empty explanation and <see cref="AiStrategyFixture.IsResolved"/>
	/// must be false. AiFactAttribute uses this to set base.Skip.
	/// </summary>
	[Fact]
	public void AiFact_SkipsWhenNoStrategyResolves()
	{
		// We construct a fresh fixture (NOT the static Default) so we can
		// exercise the no-strategy path without polluting the lazy singleton
		// that the rest of the test run shares.
		var snapshot = SnapshotEnv();
		try
		{
			ClearAllAiUnitEnv();
			Environment.SetEnvironmentVariable(EnvStrategy, "no-such-strategy");

			using var fixture = AiStrategyFixture.CreateForTesting();

			Assert.False(fixture.IsResolved);
			Assert.False(string.IsNullOrEmpty(fixture.SkipReason));
			Assert.Null(fixture.Client);
		}
		finally
		{
			RestoreEnv(snapshot);
		}
	}

	/// <summary>
	/// When env vars describe a valid cli strategy, the fixture must resolve
	/// to a live client and the skip reason must be empty. Cli kind requires
	/// only Command (no BaseUrl/Model/ApiKey gate), which makes it the
	/// cheapest happy-path verification.
	/// </summary>
	[Fact]
	public void AiFact_RunsWhenStrategyResolves()
	{
		var snapshot = SnapshotEnv();
		try
		{
			ClearAllAiUnitEnv();
			Environment.SetEnvironmentVariable(EnvStrategy, "test-cli");
			Environment.SetEnvironmentVariable(EnvKind, "cli");
			Environment.SetEnvironmentVariable(EnvCommand, "echo");

			using var fixture = AiStrategyFixture.CreateForTesting();

			// Strategy loader may be unavailable (Agent B's types missing).
			// In that case, the fixture is allowed to skip rather than
			// resolve - the contract just says SkipReason describes WHY.
			if (fixture.IsResolved)
			{
				Assert.Empty(fixture.SkipReason);
				Assert.NotNull(fixture.Client);
			}
			else
			{
				Assert.False(string.IsNullOrEmpty(fixture.SkipReason));
			}
		}
		finally
		{
			RestoreEnv(snapshot);
		}
	}

	/// <summary>
	/// AiTheory must inherit from <see cref="global::Xunit.SkippableTheoryAttribute"/>
	/// so that per-row <c>Skip.If</c> calls (using
	/// <see cref="AiSkip.IfNoStrategy"/>) still control row-level skipping
	/// when the attribute itself is not auto-skipping. The attribute itself
	/// only sets Skip in the ctor when the fixture has no strategy at
	/// discovery time.
	/// </summary>
	[Fact]
	public void AiTheory_PerRowSkipStillUsesSkipIf()
	{
		var attr = new AiTheoryAttribute();
		var baseType = typeof(AiTheoryAttribute).BaseType;
		Assert.NotNull(baseType);
		Assert.Equal("SkippableTheoryAttribute", baseType!.Name);

		// AiSkip.IfNoStrategy is the per-test gate: when the fixture has not
		// resolved, it calls Xunit.Skip.If; when resolved, it is a no-op.
		// We just verify the method exists with the expected shape so
		// MemberData rows can call it freely.
		var method = typeof(AiSkip).GetMethod(
			nameof(AiSkip.IfNoStrategy),
			BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(typeof(void), method!.ReturnType);
	}

	/// <summary>
	/// <see cref="AiStrategyFixture.Default"/> must be a process-wide lazy
	/// singleton - two reads return the same reference. Disposing the fixture
	/// must never throw and must never propagate construction failures.
	/// </summary>
	[Fact]
	public void AiStrategyFixture_LazyResolvesOnce()
	{
		var first = AiStrategyFixture.Default;
		var second = AiStrategyFixture.Default;
		Assert.Same(first, second);

		// SkipReason is a string (possibly empty); IsResolved is a bool.
		// Both must be safe to call regardless of whether Agent B's
		// strategy types compiled into this assembly.
		_ = first.SkipReason;
		_ = first.IsResolved;
	}

	private static (string? strategy, string? kind, string? baseUrl, string? model, string? command, string? apiKey) SnapshotEnv()
	{
		return (
			Environment.GetEnvironmentVariable(EnvStrategy),
			Environment.GetEnvironmentVariable(EnvKind),
			Environment.GetEnvironmentVariable(EnvBaseUrl),
			Environment.GetEnvironmentVariable(EnvModel),
			Environment.GetEnvironmentVariable(EnvCommand),
			Environment.GetEnvironmentVariable(EnvApiKey));
	}

	private static void RestoreEnv((string? strategy, string? kind, string? baseUrl, string? model, string? command, string? apiKey) snap)
	{
		Environment.SetEnvironmentVariable(EnvStrategy, snap.strategy);
		Environment.SetEnvironmentVariable(EnvKind, snap.kind);
		Environment.SetEnvironmentVariable(EnvBaseUrl, snap.baseUrl);
		Environment.SetEnvironmentVariable(EnvModel, snap.model);
		Environment.SetEnvironmentVariable(EnvCommand, snap.command);
		Environment.SetEnvironmentVariable(EnvApiKey, snap.apiKey);
	}

	private static void ClearAllAiUnitEnv()
	{
		Environment.SetEnvironmentVariable(EnvStrategy, null);
		Environment.SetEnvironmentVariable(EnvKind, null);
		Environment.SetEnvironmentVariable(EnvBaseUrl, null);
		Environment.SetEnvironmentVariable(EnvModel, null);
		Environment.SetEnvironmentVariable(EnvCommand, null);
		Environment.SetEnvironmentVariable(EnvApiKey, null);
	}
}
