using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public sealed record DeploymentRequest(TenantId TenantId, EnvironmentId EnvironmentId, TimeRange Range);
