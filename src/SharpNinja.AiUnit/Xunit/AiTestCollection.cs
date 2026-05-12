namespace SharpNinja.AiUnit.Xunit;

/// <summary>
/// Empty marker class wired up with
/// <see cref="global::Xunit.CollectionDefinitionAttribute"/> so all AI tests
/// can opt into a single, serially-executed xUnit collection. The
/// <c>DisableParallelization</c> flag prevents rate-limit collisions across
/// concurrently-running frontier model calls.
/// </summary>
[global::Xunit.CollectionDefinition(nameof(AiTestCollection), DisableParallelization = true)]
public sealed class AiTestCollection
{
}
