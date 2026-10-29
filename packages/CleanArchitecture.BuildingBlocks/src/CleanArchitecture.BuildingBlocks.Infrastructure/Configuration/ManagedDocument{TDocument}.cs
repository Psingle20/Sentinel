using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

/// <summary>
/// The current published version of a control-plane document, refreshed from its
/// <see cref="IVersionedDocumentStore{TDocument}"/>. Until a version is published the service runs
/// on its deployment configuration; afterwards the published document is applied over it.
/// </summary>
public sealed class ManagedDocument<TDocument>
    where TDocument : class
{
    private readonly Lock _gate = new();
    private CancellationTokenSource _changed = new();

    /// <summary>The newest published version this instance has seen, or <see langword="null"/>.</summary>
    public PublishedDocument<TDocument>? Current { get; private set; }

    /// <summary>Fires once when <see cref="Current"/> changes to another version.</summary>
    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return new CancellationChangeToken(_changed.Token);
        }
    }

    /// <summary>Makes <paramref name="published"/> current; a publish calls this so the change applies without waiting for a refresh.</summary>
    public void Set(PublishedDocument<TDocument>? published)
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            if (string.Equals(Current?.Version.Version, published?.Version.Version, StringComparison.Ordinal))
            {
                return;
            }

            Current = published;
            previous = _changed;
            _changed = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }
}
