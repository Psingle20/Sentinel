namespace Sentinel.Gateway.Api.Contracts;

/// <summary>A narrative edit and the version it started from; <c>0</c> for the first save.</summary>
public sealed record SaveNarrativeBody(string Markdown, int ExpectedVersion);
