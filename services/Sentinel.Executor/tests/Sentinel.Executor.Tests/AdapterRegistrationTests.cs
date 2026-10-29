using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Infrastructure.DependencyInjection;
using Shouldly;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The adapters as Executor registers them. Both are typed HTTP clients of one interface; when they shared a client
/// name, the batch adapter's address and token replaced the rollback adapter's, and every rollback inspection went to
/// the reporting worker and failed.
/// </summary>
public sealed class AdapterRegistrationTests
{
    [Fact]
    public async Task Each_adapter_calls_its_own_target_with_its_own_credential()
    {
        var handler = new RecordingHttpHandler();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Executor:Adapters:DeployController:Endpoint"] = "http://deploy-controller.test",
            ["Executor:Adapters:DeployController:Token"] = "deploy-test-not-a-token",
            ["Executor:Adapters:ReportingWorker:Endpoint"] = "http://reporting-worker.test",
            ["Executor:Adapters:ReportingWorker:ControlToken"] = "control-test-not-a-token"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddRemediationAdapters(configuration);
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => handler));
        await using ServiceProvider provider = services.BuildServiceProvider();

        IRemediationAdapter[] adapters = [.. provider.GetServices<IRemediationAdapter>()];
        adapters.Select(adapter => adapter.ActionKind).ShouldBe(["rollback", "pause-batch"], ignoreOrder: true);
        foreach (IRemediationAdapter adapter in adapters)
        {
            await Should.ThrowAsync<HttpRequestException>(() => adapter.InspectAsync(Action(adapter.ActionKind), "key", CancellationToken.None));
        }

        IReadOnlyList<(string Host, string Path, string? Bearer, string? Control)> requests = handler.Requests;
        requests.ShouldContain(request => request.Path.StartsWith("/api/services/", StringComparison.Ordinal));
        requests.Where(request => request.Path.StartsWith("/api/services/", StringComparison.Ordinal))
            .ShouldAllBe(request => request.Host == "deploy-controller.test" && request.Bearer == "deploy-test-not-a-token" && request.Control == null);
        requests.Where(request => request.Path == "/api/batch")
            .ShouldHaveSingleItem()
            .ShouldBe(("reporting-worker.test", "/api/batch", null, "control-test-not-a-token"));
    }

    private static RemediationAction Action(string kind) => kind == "rollback"
        ? new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" }
        : new PauseBatchAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("reporting-worker"), Reason = "Saturates Redis." };
}
