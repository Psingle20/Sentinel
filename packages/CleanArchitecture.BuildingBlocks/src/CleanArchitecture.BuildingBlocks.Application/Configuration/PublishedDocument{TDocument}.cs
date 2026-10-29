namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>A configuration document with the version it was published as.</summary>
public sealed record PublishedDocument<TDocument>(TDocument Document, DocumentVersion Version)
    where TDocument : class;
