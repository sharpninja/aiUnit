using Xunit.Abstractions;
using Xunit.Sdk;

namespace SharpNinja.AiUnit.Review;

/// <summary>
/// Prevents xUnit discovery from executing review agents while it enumerates
/// theory data. Review JSON is produced only when the selected test runs.
/// </summary>
public sealed class AiReviewDataDiscoverer : DataDiscoverer
{
	public override bool SupportsDiscoveryEnumeration(IAttributeInfo dataAttribute, IMethodInfo testMethod) => false;
}
