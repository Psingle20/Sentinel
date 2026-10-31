namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// Why consensus abstained (Phase E05). Only ambiguity, two explanations it could not separate on adequate evidence, may
/// ask for one more observation; too little evidence or none surviving challenge escalates to responders as before.
/// </summary>
public enum AbstentionReason
{
    Ambiguous,
    InsufficientEvidence,
    Falsified
}
