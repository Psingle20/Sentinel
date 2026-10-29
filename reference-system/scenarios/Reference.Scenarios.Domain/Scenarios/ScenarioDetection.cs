using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioDetection(bool ExpectIncident, string Service, string Metric, double Threshold, int WithinSeconds);
