namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>A recovery verdict, whether it can be trusted, the measurements behind it and the reason for anything short of recovered.</summary>
public sealed record RecoveryJudgement(bool Trustworthy, RecoveryResult Result, IReadOnlyList<Measurement> Measurements, string? Reason);
