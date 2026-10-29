using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// One investigator role (Phase 04): the tools it may call, what it looks for and how many
/// competing hypotheses it must propose. Specialists see only their own signals plus the change
/// markers they need to attribute them; the generalist sees everything.
/// </summary>
public sealed record SpecialistProfile(string Role, IReadOnlyList<string> Tools, string Focus, int MinimumHypotheses);
