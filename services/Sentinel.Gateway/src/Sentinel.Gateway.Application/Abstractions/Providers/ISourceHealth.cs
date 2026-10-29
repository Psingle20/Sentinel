using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>Ingestion's provider sources with health and watermarks, for administrators.</summary>
public interface ISourceHealth
{
    Task<ControlPlaneResponse> SourcesAsync(CancellationToken cancellationToken);
}
