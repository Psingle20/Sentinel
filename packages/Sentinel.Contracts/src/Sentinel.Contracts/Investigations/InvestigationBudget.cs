using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>Limits on one investigation (Phase 04 agent execution contract).</summary>
public sealed record InvestigationBudget(int MaxToolCalls, int MaxModelTokens, TimeSpan Timeout);
