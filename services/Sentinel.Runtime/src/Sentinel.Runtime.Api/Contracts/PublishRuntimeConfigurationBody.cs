using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Api.Contracts;

/// <summary>A new version of Runtime's control-plane document, the version it replaces and who publishes it (Phase 08).</summary>
public sealed record PublishRuntimeConfigurationBody(RuntimeConfigurationDocument Document, string ExpectedVersion, string Author);
