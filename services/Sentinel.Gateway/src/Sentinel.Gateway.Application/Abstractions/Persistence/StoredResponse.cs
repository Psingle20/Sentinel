namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>A mutation's first response, kept so a retry with the same key gets it again instead of a second change.</summary>
public sealed record StoredResponse(int StatusCode, string? ContentType, string Body);
