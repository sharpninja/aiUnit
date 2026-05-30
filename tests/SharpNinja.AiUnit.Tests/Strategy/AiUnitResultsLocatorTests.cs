using System;
using System.IO;
using SharpNinja.AiUnit.Strategy;
using Xunit;

namespace SharpNinja.AiUnit.Tests.Strategy;

/// <summary>
/// Unit tests for TR-AIUNIT-CONFIG-005/006: results output-directory and
/// online-URL resolution precedence (env &gt; config &gt; default) and the
/// sortable run-log filename format. In the env-serialized collection so the
/// AIUNIT_RESULTS_* mutations never race other suites.
/// </summary>
[Collection("StrategyEnvironment")]
public sealed class AiUnitResultsLocatorTests
{
	private const string DirEnv = "AIUNIT_RESULTS_DIR";
	private const string UrlEnv = "AIUNIT_RESULTS_BASE_URL";

	private static void ClearEnv()
	{
		Environment.SetEnvironmentVariable(DirEnv, null);
		Environment.SetEnvironmentVariable(UrlEnv, null);
	}

	[Fact]
	public void ResolveOutputDirectory_Default_IsAiunitResultsUnderBaseDirectory()
	{
		ClearEnv();

		var dir = AiUnitResultsLocator.ResolveOutputDirectory(null);

		Assert.Equal(Path.Combine(AppContext.BaseDirectory, "aiunit-results"), dir);
	}

	[Fact]
	public void ResolveOutputDirectory_UsesConfiguredValue_WhenNoEnv()
	{
		ClearEnv();

		var dir = AiUnitResultsLocator.ResolveOutputDirectory(
			new AiUnitResultsOptions(OutputDirectory: @"X:\custom-results"));

		Assert.Equal(@"X:\custom-results", dir);
	}

	[Fact]
	public void ResolveOutputDirectory_EnvOverridesConfig()
	{
		try
		{
			Environment.SetEnvironmentVariable(DirEnv, @"X:\env-results");

			var dir = AiUnitResultsLocator.ResolveOutputDirectory(
				new AiUnitResultsOptions(OutputDirectory: @"X:\cfg-results"));

			Assert.Equal(@"X:\env-results", dir);
		}
		finally
		{
			Environment.SetEnvironmentVariable(DirEnv, null);
		}
	}

	[Fact]
	public void ResolveOnlineBaseUrl_Precedence_EnvThenConfigThenNull()
	{
		ClearEnv();
		Assert.Null(AiUnitResultsLocator.ResolveOnlineBaseUrl(null));
		Assert.Equal(
			"https://logs.cfg",
			AiUnitResultsLocator.ResolveOnlineBaseUrl(new AiUnitResultsOptions(OnlineBaseUrl: "https://logs.cfg")));

		try
		{
			Environment.SetEnvironmentVariable(UrlEnv, "https://logs.env");
			Assert.Equal(
				"https://logs.env",
				AiUnitResultsLocator.ResolveOnlineBaseUrl(new AiUnitResultsOptions(OnlineBaseUrl: "https://logs.cfg")));
		}
		finally
		{
			Environment.SetEnvironmentVariable(UrlEnv, null);
		}
	}

	[Fact]
	public void BuildFileName_IsSortable_AndContainsTypeAndUtcTimestamp()
	{
		var started = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

		var name = AiUnitResultsLocator.BuildFileName("code", started);

		Assert.Equal("aiunit-review-code-20260530T120000.000Z.json", name);

		var later = AiUnitResultsLocator.BuildFileName("code", started.AddSeconds(1));
		Assert.True(string.CompareOrdinal(name, later) < 0);
	}

	[Fact]
	public void BuildFileName_NormalizesNonUtcStart_ToUtc()
	{
		var startedPlusTwo = new DateTimeOffset(2026, 5, 30, 14, 0, 0, TimeSpan.FromHours(2));

		var name = AiUnitResultsLocator.BuildFileName("plan", startedPlusTwo);

		Assert.Equal("aiunit-review-plan-20260530T120000.000Z.json", name);
	}
}
