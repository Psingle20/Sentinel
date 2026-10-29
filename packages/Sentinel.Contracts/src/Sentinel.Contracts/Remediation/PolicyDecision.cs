using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

public enum PolicyDecision
{
    Allow,
    Deny,
    ApprovalRequired,

    /// <summary>The action may be suggested to responders but Sentinel will not execute it.</summary>
    SuggestionOnly
}
