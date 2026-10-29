using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.Gateway.Tests;

/// <summary>Api's control-plane document (Phase 08): tenants, environments, notification channels and routes.</summary>
public sealed class ApiConfigurationTests
{
    private static readonly string[] AllowedHosts = ["hooks.example.test"];

    private static readonly ApiConfigurationDocument Valid = new()
    {
        Version = "api-2026.10.29.1",
        Tenants = [new TenantSetting("acme", "Acme", ["reference"])],
        Channels =
        [
            new NotificationChannelSetting("responders-log", "log", null, null),
            new NotificationChannelSetting("pager", "webhook", new Uri("https://hooks.example.test/sentinel"), "Notifications:PagerSecret")
        ],
        Routes =
        [
            new NotificationRouteSetting("incident-opened", Severity.High, "pager", "acme"),
            new NotificationRouteSetting("incident-resolved", null, "responders-log")
        ]
    };

    private static IReadOnlyList<string> Problems(ApiConfigurationDocument document) =>
        ApiConfigurationMapping.Problems(document, AllowedHosts, key => key == "Notifications:PagerSecret");

    [Fact]
    public void A_complete_document_has_no_problems()
    {
        Problems(Valid).ShouldBeEmpty();
    }

    [Fact]
    public void A_webhook_can_target_only_an_allowed_host_over_https_with_a_known_secret()
    {
        ApiConfigurationDocument document = Valid with
        {
            Channels =
            [
                new NotificationChannelSetting("elsewhere", "webhook", new Uri("https://attacker.example.test/collect"), "Notifications:PagerSecret"),
                new NotificationChannelSetting("plain", "webhook", new Uri("http://hooks.example.test/sentinel"), "Notifications:PagerSecret"),
                new NotificationChannelSetting("unsigned", "webhook", new Uri("https://hooks.example.test/sentinel"), "Notifications:Missing")
            ],
            Routes = []
        };

        IReadOnlyList<string> problems = Problems(document);

        problems.ShouldContain("Webhook host 'attacker.example.test' is not in this deployment's allowed webhook hosts.");
        problems.ShouldContain("Webhook channel 'plain' needs an absolute HTTPS address (HTTP only on loopback).");
        problems.ShouldContain("Webhook channel 'unsigned' names the configuration key of a signing secret that this deployment has.");
    }

    [Fact]
    public void Routes_must_name_a_live_event_a_defined_channel_and_a_catalogued_tenant()
    {
        ApiConfigurationDocument document = Valid with
        {
            Routes = [new NotificationRouteSetting("incident-deleted", null, "nowhere", "globex")]
        };

        IReadOnlyList<string> problems = Problems(document);

        problems.ShouldContain(problem => problem.StartsWith("Route event 'incident-deleted' is not a live event", StringComparison.Ordinal));
        problems.ShouldContain("A route for 'incident-deleted' names channel 'nowhere', which is not defined.");
        problems.ShouldContain("A route for 'incident-deleted' is limited to tenant 'globex', which is not in the catalog.");
    }

    [Fact]
    public void Every_tenant_needs_an_environment()
    {
        Problems(Valid with { Tenants = [new TenantSetting("acme", "Acme", [])] }).ShouldContain("Every tenant has an ID and at least one named environment.");
    }

    [Fact]
    public void A_tenant_scoped_route_notifies_only_for_that_tenant()
    {
        var options = new NotificationOptions();
        ApiConfigurationMapping.Apply(options, Valid);
        var acme = new LiveEventDraft(TenantId.From("acme"), EnvironmentId.From("reference"), IncidentId.From(Guid.NewGuid()), LiveEventKinds.IncidentOpened, 1, "Incident opened", Severity.Critical, DateTimeOffset.UnixEpoch);

        NotificationRouter.Route(acme, options, Guid.NewGuid).Select(message => message.Channel).ShouldBe(["pager"]);
        NotificationRouter.Route(acme with { TenantId = TenantId.From("globex") }, options, Guid.NewGuid).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_publish_that_would_point_a_webhook_elsewhere_is_never_stored()
    {
        var store = new InMemoryStore();
        var service = new ApiConfigurationService(
            new VersionedDocumentPublisher<ApiConfigurationDocument>(store, TimeProvider.System),
            () => ApiConfigurationMapping.From(new TenantCatalogOptions(), new NotificationOptions()),
            AllowedHosts,
            _ => true);

        Result<PublishedDocument<ApiConfigurationDocument>> refused = await service.PublishAsync(
            Valid with { Channels = [new NotificationChannelSetting("pager", "webhook", new Uri("https://attacker.example.test/"), "Notifications:PagerSecret")], Routes = [] },
            ApiConfigurationMapping.DeploymentVersion,
            "operator@example.test",
            CancellationToken.None);
        Result<PublishedDocument<ApiConfigurationDocument>> published = await service.PublishAsync(Valid, ApiConfigurationMapping.DeploymentVersion, "operator@example.test", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        published.IsSuccess.ShouldBeTrue();
        store.Versions.Count.ShouldBe(1);
    }

    private sealed class InMemoryStore : IVersionedDocumentStore<ApiConfigurationDocument>
    {
        public List<PublishedDocument<ApiConfigurationDocument>> Versions { get; } = [];

        public Task<PublishedDocument<ApiConfigurationDocument>?> CurrentAsync(CancellationToken cancellationToken) => Task.FromResult(Versions.LastOrDefault());

        public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DocumentVersion>>([.. Versions.Select(version => version.Version).Reverse()]);

        public Task<AppendOutcome> AppendAsync(ApiConfigurationDocument document, DocumentVersion version, CancellationToken cancellationToken)
        {
            Versions.Add(new PublishedDocument<ApiConfigurationDocument>(document, version));
            return Task.FromResult(AppendOutcome.Appended);
        }
    }
}
