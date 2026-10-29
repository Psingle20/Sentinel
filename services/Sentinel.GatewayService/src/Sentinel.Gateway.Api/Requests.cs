using System.Text.Json;

namespace Sentinel.Gateway.Api;

/// <summary>The plan version and content hash the approver reviewed.</summary>
public sealed record ApprovePlanRequest(int PlanVersion, string PlanHash);

/// <summary>A rejection or revocation of one plan version, with the reason.</summary>
public sealed record RejectPlanRequest(int PlanVersion, string Reason);

/// <summary>A narrative edit and the version it started from; <c>0</c> for the first save.</summary>
public sealed record SaveNarrativeRequest(string Markdown, int ExpectedVersion);

/// <summary>A new policy document and the version the operator started from; Executor validates it.</summary>
public sealed record PublishPolicyRequest(JsonElement Document, string ExpectedVersion);

/// <summary>Engages or releases an operator control.</summary>
public sealed record ControlRequest(bool Enabled);

/// <summary>A new version of a configuration area's document and the version it replaces (Phase 08).</summary>
public sealed record PublishConfigurationRequest(JsonElement Document, string ExpectedVersion);
