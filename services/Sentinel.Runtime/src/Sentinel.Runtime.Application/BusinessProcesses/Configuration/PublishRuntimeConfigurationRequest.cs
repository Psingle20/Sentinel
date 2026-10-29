using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

/// <summary>A new version of Runtime's control-plane document, which the Gateway authorized (Phase 08).</summary>
public sealed record PublishRuntimeConfigurationRequest(RuntimeConfigurationDocument Document, string ExpectedVersion, string Author) : IRequest<DocumentVersion>;
