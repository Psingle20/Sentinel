namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>One published version of a configuration document: who published it, when, and which version it replaced.</summary>
public sealed record DocumentVersion(string Version, string Author, DateTimeOffset PublishedAt, string? BasedOn);
