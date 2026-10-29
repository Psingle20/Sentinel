using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

/// <summary>Runtime's current control-plane document (Phase 08).</summary>
public sealed record GetRuntimeConfigurationRequest : IRequest<DocumentState<RuntimeConfigurationDocument>>;
