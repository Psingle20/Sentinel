using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioRecovery(string RestoredBy, IReadOnlyList<RecoveryExpectation> Criteria, int ObservationWindowSeconds, int EvaluationTimeoutSeconds);
