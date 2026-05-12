using System;

namespace SharpNinja.AiUnit.Validation;

/// <summary>
/// Thrown by <see cref="AiUnitJsonAssertions"/> helpers when a frontier model
/// response does not satisfy the structural contract the test expects. The
/// exception is caught by xUnit and surfaces as a normal test failure with
/// the offending field name and reason in the message.
/// </summary>
public sealed class AiResponseValidationException : Exception
{
	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="AiResponseValidationException"/> class with a message
	/// describing the contract violation.
	/// </summary>
	/// <param name="message">Human-readable description of the failure.</param>
	public AiResponseValidationException(string message) : base(message)
	{
	}

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="AiResponseValidationException"/> class with a message
	/// and an inner exception (typically a
	/// <see cref="System.Text.Json.JsonException"/>).
	/// </summary>
	/// <param name="message">Human-readable description of the failure.</param>
	/// <param name="inner">The exception that triggered this validation failure.</param>
	public AiResponseValidationException(string message, Exception inner) : base(message, inner)
	{
	}
}
