using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>How long incident memory is kept and who may see it (Phase 07 retention and privacy metadata).</summary>
public sealed record RetentionMetadata(
    DateTimeOffset RetainUntil,
    bool LegalHold,
    string PrivacyClass,
    string RedactionPolicyVersion,
    DateTimeOffset? DeletedAt = null,
    string? DeletedBy = null,
    string? DeletionReason = null);
