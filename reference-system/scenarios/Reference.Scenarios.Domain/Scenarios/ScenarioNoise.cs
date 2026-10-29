using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioNoise(double TrafficRequestsPerSecond, int Seed, double LatencyJitter, int TimingToleranceSeconds, string Notes);
