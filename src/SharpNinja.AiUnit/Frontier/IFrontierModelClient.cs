using System.Threading;
using System.Threading.Tasks;

namespace SharpNinja.AiUnit.Frontier;

/// <summary>
/// Provider-agnostic contract for cloud frontier-model adapters. aiUnit ships
/// HTTP adapters for Anthropic Claude, OpenAI-compatible (OpenAI + xAI + MAF
/// + Cline), and Google Gemini, plus a CLI adapter that spawns the local
/// <c>claude</c> or <c>codex</c> binary.
///
/// Implementations MUST translate transport / parsing / auth failures into a
/// non-null <see cref="FrontierResponse.Error"/> rather than throwing.
/// Callers treat any non-null Error as "skip this regression test" or
/// "fall back to the deterministic path".
/// </summary>
public interface IFrontierModelClient
{
	/// <summary>Lower-case short name of the provider ("anthropic", "openai", "xai", "google", "cli").</summary>
	string Provider { get; }

	/// <summary>User-configured model identifier (e.g. "grok-4-latest", "gpt-5", "gemini-2.5-pro", "claude-opus-4-5").</summary>
	string ModelVersion { get; }

	/// <summary>
	/// Submit a request to the frontier model. Returns a populated
	/// <see cref="FrontierResponse"/> on success or a response with non-null
	/// <see cref="FrontierResponse.Error"/> on failure. Throws
	/// <see cref="System.OperationCanceledException"/> only when the
	/// caller-supplied <paramref name="cancellationToken"/> is cancelled.
	/// </summary>
	Task<FrontierResponse> SendAsync(
		FrontierRequest request,
		CancellationToken cancellationToken = default);
}
