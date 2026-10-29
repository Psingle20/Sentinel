using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

/// <summary>A PromQL expression and what it must show between two offsets from the first step.</summary>
public sealed record SignalExpectation(string Description, string Query, string Expectation, double Threshold, int FromSeconds, int WithinSeconds);
