using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record CheckResult(string Name, bool Passed, string Detail);
