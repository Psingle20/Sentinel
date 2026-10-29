using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioHypotheses(HypothesisExpectation Leading, IReadOnlyList<HypothesisExpectation> Rejected);
