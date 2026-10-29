using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record RecoveryExpectation(string Description, string Query, string Comparison, double Threshold);
