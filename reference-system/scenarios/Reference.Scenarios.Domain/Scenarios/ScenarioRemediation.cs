using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioRemediation(IReadOnlyList<RemediationExpectation> Permitted, IReadOnlyList<RemediationExpectation> FalseRemediations);
