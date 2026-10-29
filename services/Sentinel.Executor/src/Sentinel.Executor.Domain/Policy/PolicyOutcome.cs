using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

public sealed record PolicyOutcome(PolicyVerdict Verdict, string PolicyVersion, IReadOnlyList<string> RuleTrace);
