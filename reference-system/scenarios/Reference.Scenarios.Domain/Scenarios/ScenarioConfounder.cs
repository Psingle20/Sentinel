using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioConfounder(string Description, string Service, string Change, int AtSeconds, int? ServingFromSeconds);
