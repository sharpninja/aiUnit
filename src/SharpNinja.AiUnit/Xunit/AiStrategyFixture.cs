using System;
using System.Linq;
using System.Reflection;
using SharpNinja.AiUnit.Frontier;

namespace SharpNinja.AiUnit.Xunit;

/// <summary>
/// Process-wide, lazily-constructed singleton that holds the resolved
/// frontier client (if any) for the AI test suite. Test classes use it via
/// <see cref="Default"/>; new instances are obtained via
/// <see cref="CreateForTesting"/> for hermetic env-var manipulation in unit
/// tests of the fixture itself.
///
/// The fixture deliberately catches every construction failure and surfaces
/// it as a non-empty <see cref="SkipReason"/> instead of throwing. xUnit
/// discovery would explode if <see cref="Default"/> threw; the
/// auto-skip-on-no-strategy contract relies on the fixture always
/// successfully constructing.
///
/// Strategy types (<c>AiUnitStrategyLoader</c>, <c>AiUnitStrategyResolver</c>,
/// <c>ResolvedStrategy</c>) live in the sibling
/// <c>SharpNinja.AiUnit.Strategy</c> namespace. To keep Phase 4 buildable in
/// isolation from Phase 3, the fixture uses reflection to invoke them: when
/// the types are present the fixture binds normally; when absent it sets
/// <see cref="SkipReason"/> to a clear "strategy types missing" message.
/// </summary>
public sealed class AiStrategyFixture : IDisposable
{
	private const string StrategyNamespace = "SharpNinja.AiUnit.Strategy";
	private const string LoaderTypeName = StrategyNamespace + ".AiUnitStrategyLoader";
	private const string ResolverTypeName = StrategyNamespace + ".AiUnitStrategyResolver";

	private static readonly Lazy<AiStrategyFixture> s_default = new(
		() => new AiStrategyFixture(snapshotEnv: false),
		isThreadSafe: true);

	private readonly IFrontierModelClient? _client;
	private readonly object? _resolved;
	private readonly string _skipReason;
	private bool _disposed;

	/// <summary>
	/// Process-wide singleton. Constructed on first access; safe to call
	/// from any thread.
	/// </summary>
	public static AiStrategyFixture Default => s_default.Value;

	/// <summary>
	/// Factory for tests of the fixture itself: each call returns a fresh
	/// instance so unit tests can manipulate env vars without polluting the
	/// shared <see cref="Default"/>.
	/// </summary>
	/// <returns>A new fixture bound to the current process env state.</returns>
	public static AiStrategyFixture CreateForTesting() => new(snapshotEnv: false);

	private AiStrategyFixture(bool snapshotEnv)
	{
		_ = snapshotEnv;
		try
		{
			var (client, resolved, skip) = TryResolveViaReflection();
			_client = client;
			_resolved = resolved;
			_skipReason = skip ?? string.Empty;
		}
		catch (Exception ex)
		{
			_client = null;
			_resolved = null;
			_skipReason = "AiStrategyFixture construction failed: " + ex.Message;
		}
	}

	/// <summary>
	/// Resolved frontier client, or null when no strategy was selected /
	/// every strategy reported a skip reason.
	/// </summary>
	public IFrontierModelClient? Client => _client;

	/// <summary>
	/// Strongly-boxed resolved-strategy view for telemetry. Returns null when
	/// <see cref="Strategy.ResolvedStrategy"/> (Agent B) is not yet available
	/// in the loaded assemblies; in that case <see cref="SkipReason"/> is
	/// populated with the same diagnostic. Callers that need the typed view
	/// cast to the runtime type directly.
	/// </summary>
	public object? Resolved => _resolved;

	/// <summary>
	/// Human-readable reason the strategy did not resolve. Empty when a
	/// client is available. Populated by Agent B's resolver, the
	/// loader-missing fallback, or an unhandled construction exception.
	/// </summary>
	public string SkipReason => _skipReason;

	/// <summary>True when a frontier client is available for AI tests.</summary>
	public bool IsResolved => _client is not null;

	/// <summary>
	/// Disposes the underlying client if it implements
	/// <see cref="IDisposable"/> (HTTP adapters typically own an
	/// <see cref="System.Net.Http.HttpClient"/>).
	/// </summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (_client is IDisposable disposable)
		{
			try { disposable.Dispose(); } catch { /* swallow */ }
		}
	}

	private static (IFrontierModelClient? Client, object? Resolved, string? Skip) TryResolveViaReflection()
	{
		var loaderType = FindType(LoaderTypeName);
		var resolverType = FindType(ResolverTypeName);
		if (loaderType is null || resolverType is null)
		{
			return (null, null, "AiUnit strategy types not loaded (Phase 3 not yet shipped).");
		}

		// AiUnitStrategyConfig? AiUnitStrategyLoader.TryLoad(string? overridePath = null)
		var tryLoad = loaderType.GetMethod(
			"TryLoad",
			BindingFlags.Public | BindingFlags.Static);
		if (tryLoad is null)
		{
			return (null, null, "AiUnitStrategyLoader.TryLoad not found on the loaded type.");
		}
		object? config = null;
		try
		{
			var parameters = tryLoad.GetParameters().Length == 0
				? Array.Empty<object?>()
				: new object?[] { null };
			config = tryLoad.Invoke(null, parameters);
		}
		catch (TargetInvocationException tie) when (tie.InnerException is not null)
		{
			return (null, null, "AiUnitStrategyLoader.TryLoad threw: " + tie.InnerException.Message);
		}

		// (string Name, AiUnitStrategyConfig? Cfg) AiUnitStrategyLoader.ResolveActive(config)
		var resolveActive = loaderType.GetMethod(
			"ResolveActive",
			BindingFlags.Public | BindingFlags.Static);
		if (resolveActive is null)
		{
			return (null, null, "AiUnitStrategyLoader.ResolveActive not found.");
		}
		object? active;
		try
		{
			active = resolveActive.Invoke(null, new object?[] { config });
		}
		catch (TargetInvocationException tie) when (tie.InnerException is not null)
		{
			return (null, null, "AiUnitStrategyLoader.ResolveActive threw: " + tie.InnerException.Message);
		}
		if (active is null)
		{
			return (null, null, "AiUnitStrategyLoader.ResolveActive returned null.");
		}

		// active is a ValueTuple<string, ...>; extract Item1 + Item2 by name.
		var activeType = active.GetType();
		var nameField = activeType.GetField("Item1");
		var cfgField = activeType.GetField("Item2");
		if (nameField is null || cfgField is null)
		{
			return (null, null, "ResolveActive return shape did not match expected ValueTuple<Name, Cfg>.");
		}
		var strategyName = nameField.GetValue(active) as string ?? "(unresolved)";
		var strategyCfg = cfgField.GetValue(active);

		// (IFrontierModelClient? Client, ResolvedStrategy Resolved, string SkipReason)
		// AiUnitStrategyResolver.Build(string name, AiUnitStrategySettings? settings, ...)
		// First two params are required; any additional params have defaults
		// (e.g., IHttpClientFactory? = null). Pick the Build with the highest
		// param count so all optional-with-defaults get null/Type.Missing.
		var build = resolverType
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.Where(m => m.Name == "Build" && m.GetParameters().Length >= 2)
			.OrderByDescending(m => m.GetParameters().Length)
			.FirstOrDefault();
		if (build is null)
		{
			return (null, null, "AiUnitStrategyResolver.Build(string, settings, ...) not found.");
		}

		object? buildResult;
		try
		{
			var parameters = build.GetParameters();
			var args = new object?[parameters.Length];
			args[0] = strategyName;
			args[1] = strategyCfg;
			for (var i = 2; i < parameters.Length; i++)
			{
				args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
			}
			buildResult = build.Invoke(null, args);
		}
		catch (TargetInvocationException tie) when (tie.InnerException is not null)
		{
			return (null, null, "AiUnitStrategyResolver.Build threw: " + tie.InnerException.Message);
		}
		if (buildResult is null)
		{
			return (null, null, "AiUnitStrategyResolver.Build returned null.");
		}

		var resultType = buildResult.GetType();
		var clientField = resultType.GetField("Item1");
		var resolvedField = resultType.GetField("Item2");
		var skipField = resultType.GetField("Item3");
		if (clientField is null || resolvedField is null || skipField is null)
		{
			return (null, null, "Resolver Build return shape did not match expected ValueTuple<Client, Resolved, Skip>.");
		}

		var client = clientField.GetValue(buildResult) as IFrontierModelClient;
		var resolved = resolvedField.GetValue(buildResult);
		var skip = skipField.GetValue(buildResult) as string ?? string.Empty;
		if (string.IsNullOrEmpty(skip) && client is null)
		{
			skip = "AiUnitStrategyResolver.Build returned null client without a skip reason.";
		}
		return (client, resolved, skip);
	}

	private static Type? FindType(string fullName)
	{
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type? t;
			try
			{
				t = asm.GetType(fullName, throwOnError: false);
			}
			catch
			{
				continue;
			}
			if (t is not null)
			{
				return t;
			}
		}
		return Type.GetType(fullName, throwOnError: false);
	}
}
