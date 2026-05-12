using Xunit;

namespace SharpNinja.AiUnit.Tests.Strategy;

/// <summary>
/// xUnit collection definition: tests that mutate AIUNIT_* environment
/// variables (the strategy resolver suite, plus any future env-sensitive
/// suites) declare <c>[Collection("StrategyEnvironment")]</c> so they share
/// this collection and never run in parallel with each other.
///
/// Sibling test classes outside this collection (e.g. HTTP adapter tests
/// that do not touch the env) keep parallel execution and are unaffected.
/// </summary>
[CollectionDefinition("StrategyEnvironment", DisableParallelization = true)]
public sealed class StrategyEnvironmentCollection { }
