using System.Globalization;

namespace Reference.Scenarios.Application.Runs;

public sealed record StepExecution(int AtSeconds, string Action, string Service, string? Version, string Role, DateTimeOffset? ExecutedAt, string? Error);
