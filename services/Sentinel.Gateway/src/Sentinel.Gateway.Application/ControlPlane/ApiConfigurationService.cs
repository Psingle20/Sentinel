using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>Reads and publishes Api's control-plane document; the deployment configuration seeds it.</summary>
public sealed class ApiConfigurationService(
    VersionedDocumentPublisher<ApiConfigurationDocument> publisher,
    Func<ApiConfigurationDocument> seed,
    IReadOnlyCollection<string> allowedWebhookHosts,
    Func<string, bool> secretExists)
{
    public Task<DocumentState<ApiConfigurationDocument>> CurrentAsync(CancellationToken cancellationToken) =>
        publisher.CurrentAsync(seed, cancellationToken);

    public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        publisher.HistoryAsync(seed, 100, cancellationToken);

    public Task<Result<PublishedDocument<ApiConfigurationDocument>>> PublishAsync(ApiConfigurationDocument document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        publisher.PublishAsync(document, expectedVersion, author, seed, candidate => ApiConfigurationMapping.Problems(candidate, allowedWebhookHosts, secretExists), cancellationToken);
}
