using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

public sealed record ProviderHealth(string Provider, ProviderStatus Status);
