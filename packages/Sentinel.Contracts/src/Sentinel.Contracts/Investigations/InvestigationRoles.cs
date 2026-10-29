using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>Specialist roles Runtime can assign (Phase 04) and the adversarial verifier (Phase 05).</summary>
public static class InvestigationRoles
{
    public const string Generalist = "generalist";
    public const string Metrics = "metrics";
    public const string Logs = "logs";
    public const string Traces = "traces";
    public const string Changes = "changes";
    public const string Topology = "topology";
    public const string Knowledge = "knowledge";
    public const string Verifier = "verifier";

    public static readonly IReadOnlyList<string> Specialists = [Metrics, Logs, Traces, Changes, Topology, Knowledge];

    public static bool IsKnown(string? role) => role is null || role == Generalist || role == Verifier || Specialists.Contains(role);
}
