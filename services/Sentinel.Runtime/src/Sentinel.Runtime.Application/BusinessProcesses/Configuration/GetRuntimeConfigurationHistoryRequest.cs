using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

/// <summary>Every published version of Runtime's control-plane document (Phase 08).</summary>
public sealed record GetRuntimeConfigurationHistoryRequest : IRequest<IReadOnlyList<DocumentVersion>>;
