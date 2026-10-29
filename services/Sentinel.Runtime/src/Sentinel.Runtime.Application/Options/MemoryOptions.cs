using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.Options;

/// <summary>Configured as <c>Runtime:Memory</c> (Phase 07).</summary>
public sealed class MemoryOptions
{
    /// <summary>How long a remembered incident is kept before a purge may remove it; a legal hold keeps it longer.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(400);

    public string PrivacyClass { get; set; } = PrivacyClasses.Internal;

    /// <summary>The redaction policy applied to the observations memory statements were derived from.</summary>
    public string RedactionPolicyVersion { get; set; } = "redaction-v1";

    /// <summary>The most similar incidents offered to one investigation.</summary>
    public int SimilarLimit { get; set; } = 3;

    public double MinimumSimilarity { get; set; } = 0.3;

    /// <summary>Whether retrieval may return incidents of another environment of the same tenant. It never crosses tenants.</summary>
    public bool CrossEnvironment { get; set; }

    /// <summary>Privacy classes whose memory is never offered as a hint to an investigation.</summary>
    public IList<string> ExcludedFromRetrieval { get; set; } = [PrivacyClasses.Restricted];
}
