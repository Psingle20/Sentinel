namespace CleanArchitecture.BuildingBlocks.Configuration;

/// <summary>A configuration document that names its own version.</summary>
public interface IVersionedDocument
{
    string Version { get; }
}

/// <summary>The document in effect: the newest published version, or the deployment's seed when none is published.</summary>
public sealed record DocumentState<TDocument>(TDocument Document, DocumentVersion Version, bool Published)
    where TDocument : class;

/// <summary>Why a publish was refused.</summary>
public static class VersionedDocumentErrors
{
    public static readonly Error VersionConflict = Error.Conflict("Configuration.VersionConflict", "The configuration changed since it was read; reload it and apply the change again.");

    public static readonly Error VersionUnchanged = Error.Problem("Configuration.VersionUnchanged", "A published configuration needs a new version.");

    public static readonly Error DuplicateVersion = Error.Conflict("Configuration.DuplicateVersion", "That version was published before; versions are never reused.");

    public static Error Invalid(IReadOnlyList<string> problems) => Error.Problem("Configuration.Invalid", string.Join(" ", problems));
}

/// <summary>
/// Publishing rules shared by every control-plane document: a change names the version it was read
/// as, gets a version of its own, passes the owner's validation, and is appended only if nothing was
/// published in between. Until the first publish the deployment's seed document is in effect.
/// </summary>
public sealed class VersionedDocumentPublisher<TDocument>(IVersionedDocumentStore<TDocument> store, TimeProvider time)
    where TDocument : class, IVersionedDocument
{
    /// <summary>The author recorded for the seed, which comes from the deployment rather than a person.</summary>
    public const string SeedAuthor = "deployment configuration";

    public async Task<DocumentState<TDocument>> CurrentAsync(Func<TDocument> seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (await store.CurrentAsync(cancellationToken) is { } published)
        {
            return new DocumentState<TDocument>(published.Document, published.Version, true);
        }

        TDocument document = seed();
        return new DocumentState<TDocument>(document, new DocumentVersion(document.Version, SeedAuthor, DateTimeOffset.MinValue, null), false);
    }

    /// <summary>Published versions, newest first, ending with the seed they all descend from.</summary>
    public async Task<IReadOnlyList<DocumentVersion>> HistoryAsync(Func<TDocument> seed, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        IReadOnlyList<DocumentVersion> published = await store.HistoryAsync(limit, cancellationToken);
        return [.. published, new DocumentVersion(seed().Version, SeedAuthor, DateTimeOffset.MinValue, null)];
    }

    public async Task<Result<PublishedDocument<TDocument>>> PublishAsync(
        TDocument document,
        string expectedVersion,
        string author,
        Func<TDocument> seed,
        Func<TDocument, IReadOnlyList<string>> validate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);
        ArgumentNullException.ThrowIfNull(validate);

        DocumentState<TDocument> current = await CurrentAsync(seed, cancellationToken);
        if (!string.Equals(current.Version.Version, expectedVersion, StringComparison.Ordinal))
        {
            return Result.Failure<PublishedDocument<TDocument>>(VersionedDocumentErrors.VersionConflict);
        }

        if (string.IsNullOrWhiteSpace(document.Version) || string.Equals(document.Version, current.Version.Version, StringComparison.Ordinal))
        {
            return Result.Failure<PublishedDocument<TDocument>>(VersionedDocumentErrors.VersionUnchanged);
        }

        IReadOnlyList<string> problems = validate(document);
        if (problems.Count > 0)
        {
            return Result.Failure<PublishedDocument<TDocument>>(VersionedDocumentErrors.Invalid(problems));
        }

        var version = new DocumentVersion(document.Version, author, time.GetUtcNow(), current.Version.Version);
        return await store.AppendAsync(document, version, cancellationToken) switch
        {
            AppendOutcome.Appended => Result.Success(new PublishedDocument<TDocument>(document, version)),
            AppendOutcome.DuplicateVersion => Result.Failure<PublishedDocument<TDocument>>(VersionedDocumentErrors.DuplicateVersion),
            _ => Result.Failure<PublishedDocument<TDocument>>(VersionedDocumentErrors.VersionConflict)
        };
    }
}
