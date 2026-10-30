using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

/// <summary>The alerts active for a tenant and environment as of <see cref="At"/>.</summary>
public sealed record AlertRequest(TenantId TenantId, EnvironmentId EnvironmentId, DateTimeOffset At);
