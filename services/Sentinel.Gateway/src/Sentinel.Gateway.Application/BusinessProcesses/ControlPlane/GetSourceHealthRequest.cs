using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>Ingestion's source health and watermarks, as Ingestion answers.</summary>
public sealed record GetSourceHealthRequest : IRequest<ControlPlaneResponse>;
