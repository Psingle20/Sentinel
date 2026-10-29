using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public sealed record TopologyRequest(TenantId TenantId, EnvironmentId EnvironmentId, TimeRange Range);
