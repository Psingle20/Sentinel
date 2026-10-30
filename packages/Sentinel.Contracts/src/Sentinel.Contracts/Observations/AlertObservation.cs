namespace Sentinel.Contracts.Observations;

/// <summary>
/// An alert a monitoring system raised (Phase 02), as it stood when Ingestion read it: pending while its condition holds
/// for less than the rule's hold time, firing after. Each state an alert reaches is its own observation, so a pending
/// alert that starts firing adds one; its <see cref="Summary"/> is the rule's own text and, like any log line, untrusted.
/// </summary>
public sealed record AlertObservation : Observation
{
    /// <summary>The alerting rule's name, for example <c>CheckoutSloFastBurn</c>.</summary>
    public required string AlertName { get; init; }

    /// <summary><c>pending</c> or <c>firing</c>.</summary>
    public required string State { get; init; }

    /// <summary>The severity the rule labels the alert with, when it does.</summary>
    public string? Severity { get; init; }

    /// <summary>When the alert's condition began to hold.</summary>
    public required DateTimeOffset ActiveAt { get; init; }

    /// <summary>The value of the rule's expression when the alert was read.</summary>
    public double? Value { get; init; }

    public string? Summary { get; init; }
}
