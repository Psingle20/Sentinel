using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

public sealed record AuditEntry(long Sequence, string Actor, string Action, string Target, JsonElement Detail, DateTimeOffset OccurredAt);
