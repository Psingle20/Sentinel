namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>The document in effect: the newest published version, or the deployment's seed when none is published.</summary>
public sealed record DocumentState<TDocument>(TDocument Document, DocumentVersion Version, bool Published)
    where TDocument : class;
