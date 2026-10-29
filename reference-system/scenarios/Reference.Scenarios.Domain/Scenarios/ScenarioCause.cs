using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioCause(string Description, string Service, string Change, int ActivationSeconds);
