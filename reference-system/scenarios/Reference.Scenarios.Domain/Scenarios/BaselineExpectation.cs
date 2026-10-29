using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

/// <summary><c>rollback</c>, <c>abstain</c> or <c>noIncident</c>, and whether that outcome is correct.</summary>
public sealed record BaselineExpectation(string Outcome, string? Service, string? FromVersion, string? ToVersion, bool Correct);
