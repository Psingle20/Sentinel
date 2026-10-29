namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>
/// An append-only history of one configuration document. Published versions are never edited or
/// removed, so anything that recorded the version it used can look it up later. A version is
/// appended only if the version it was based on is still the newest, which keeps the history linear
/// when two people publish at once.
/// </summary>
public interface IVersionedDocumentStore<TDocument>
    where TDocument : class
{
    /// <summary>The newest published version, or <see langword="null"/> when nothing has been published.</summary>
    Task<PublishedDocument<TDocument>?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>Published versions, newest first.</summary>
    Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Appends <paramref name="document"/> as <paramref name="version"/> when <see cref="DocumentVersion.BasedOn"/>
    /// is the newest published version, or when nothing has been published yet.
    /// </summary>
    Task<AppendOutcome> AppendAsync(TDocument document, DocumentVersion version, CancellationToken cancellationToken);
}
