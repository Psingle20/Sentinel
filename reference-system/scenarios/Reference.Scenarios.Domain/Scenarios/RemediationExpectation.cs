using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record RemediationExpectation(string Action, string Service, string? FromVersion = null, string? ToVersion = null, string? Mode = null);
