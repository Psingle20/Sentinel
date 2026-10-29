using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public sealed record LogRangeRequest(TenantId TenantId, EnvironmentId EnvironmentId, LogSourceDefinition Source, TimeRange Range);
