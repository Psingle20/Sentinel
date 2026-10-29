using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>A replay answer from AgentWorker: the result, or why it was refused.</summary>
public sealed record AgentReplayResponse(InvestigationReplayResult? Result, int StatusCode, string? Problem);
