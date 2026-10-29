namespace Sentinel.Runtime.Api.Contracts;

/// <summary>An operator places or lifts a legal hold on an incident's memory.</summary>
public sealed record LegalHoldBody(bool Hold, string Actor);