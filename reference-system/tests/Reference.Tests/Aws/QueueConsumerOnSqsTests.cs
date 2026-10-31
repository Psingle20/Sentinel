using System.Diagnostics;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.SQS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Reference.Scenarios.Infrastructure.Environments.Aws;
using Reference.Scenarios.Infrastructure.Environments.Local;
using Shouldly;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Reference.Tests.Aws;

/// <summary>
/// The real queue-consumer, published as the image the AWS environment runs, as an ECS task on the emulator's SQS queue:
/// it receives the orders its own producer publishes, books them and confirms each one with order-service, here a stub
/// on this machine that the task reaches through the container host. This is the workload half of Stage 1; the
/// environment half is <see cref="AwsReferenceEnvironmentTests"/>.
/// </summary>
[Trait("Category", "Emulator")]
[Collection(EmulatorCollection.Name)]
public sealed class QueueConsumerOnSqsTests(FlociEmulator emulator, ITestOutputHelper output) : IClassFixture<FlociEmulator>
{
    private const string Repository = "reference-queue-consumer-conformance";

    [Fact]
    public async Task The_consumer_image_processes_orders_from_sqs_and_confirms_them_with_order_service()
    {
        await PublishImageAsync();
        int confirmations = 0;
        await using WebApplication orders = await StartOrderServiceStubAsync(() => Interlocked.Increment(ref confirmations));
        Uri ordersAddress = new(orders.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());

        string suffix = Guid.NewGuid().ToString("N")[..8];
        var options = new AwsEnvironmentOptions
        {
            ServiceUrl = emulator.Endpoint,
            Cluster = $"reference-{suffix}",
            Rule = $"orders-route-{suffix}",
            Image = $"{Repository}:{{revision}}",
            TaskEnvironment = new Dictionary<string, string>
            {
                ["Reference__Queue__Provider"] = "sqs",
                ["Reference__Queue__Sqs__ServiceUrl"] = "http://aws-emulator:4566",
                ["Reference__Queue__Publish"] = "eventbridge",
                ["Reference__Queue__EventBridge__ServiceUrl"] = "http://aws-emulator:4566",
                ["services__order-service__http__0"] = AwsEnvironmentOptions.ForContainers(ordersAddress.ToString(), "host.docker.internal")
            }
        };
        using AmazonSQSClient sqs = emulator.Sqs();
        using AmazonECSClient ecs = emulator.Ecs();
        using AmazonEventBridgeClient events = emulator.Events();
        using var provisioning = new AwsProvisioning();
        var environment = new AwsReferenceEnvironment(sqs, ecs, events, options, provisioning, new LocalReferenceEnvironment(new NoReferenceControl()), TimeProvider.System, NullLogger<AwsReferenceEnvironment>.Instance);

        await environment.ResetAsync(CancellationToken.None);

        var waited = Stopwatch.StartNew();
        while (Volatile.Read(ref confirmations) < 20 && waited.Elapsed < TimeSpan.FromSeconds(90))
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        output.WriteLine($"{confirmations} confirmations after {waited.Elapsed.TotalSeconds:F1} s.");
        Volatile.Read(ref confirmations).ShouldBeGreaterThanOrEqualTo(20, "the consumer receives its orders from SQS and confirms them");
        (await environment.InspectAsync(CancellationToken.None)).Ready.ShouldBeTrue();
    }

    private async Task PublishImageAsync()
    {
        string project = Path.Combine(RepositoryRoot(), "reference-system", "services", "Reference.QueueConsumer", "Reference.QueueConsumer.csproj");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "publish", project, "-t:PublishContainer", "-c", "Release", $"-p:ContainerRepository={Repository}", "--nologo", "-v", "quiet" })
        {
            start.ArgumentList.Add(argument);
        }

        var published = Stopwatch.StartNew();
        using Process publish = Process.Start(start)!;
        Task<string> error = publish.StandardError.ReadToEndAsync();
        string log = await publish.StandardOutput.ReadToEndAsync();
        await publish.WaitForExitAsync();
        output.WriteLine($"Published {Repository} in {published.Elapsed.TotalSeconds:F0} s.");
        publish.ExitCode.ShouldBe(0, log + await error);
    }

    private static async Task<WebApplication> StartOrderServiceStubAsync(Action confirmed)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        app.MapPost("/api/orders/{orderId}/confirmation", (string orderId) =>
        {
            confirmed();
            return Results.NoContent();
        });
        await app.StartAsync();
        return app;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sentinel.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root, with Sentinel.slnx, was not found above the test output.");
    }

    private sealed class NoReferenceControl : Scenarios.Application.Abstractions.IReferenceControl
    {
        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeployAsync(Scenarios.Domain.Scenarios.ScenarioStep step, string runId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartBatchAsync(Scenarios.Domain.Scenarios.ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InjectPoisonMessageAsync(Scenarios.Domain.Scenarios.ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DuplicatePublicationAsync(Scenarios.Domain.Scenarios.ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SlowConfirmationsAsync(Scenarios.Domain.Scenarios.ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PlantLogAsync(Scenarios.Domain.Scenarios.ScenarioStep step, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
