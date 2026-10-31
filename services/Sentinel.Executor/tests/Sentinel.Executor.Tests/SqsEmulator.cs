using System.Text.Json;
using Amazon.EventBridge;
using Amazon.Runtime;
using Amazon.SQS;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The AWS emulator ADR-021 pins, started for Executor's SQS adapter tests (Phase E01, Stage 2). Only SQS is used, so
/// it needs neither the Docker socket nor a network of its own. Its digest is read from the reference system's
/// <c>emulator.json</c>, the one place it is written.
/// </summary>
public sealed class SqsEmulator : IAsyncLifetime
{
    public const string Account = "000000000000";
    public const string Region = "us-east-1";

    // Synthetic and never validated: the emulator does not check signatures, and the access key only selects the account.
    public const string Secret = "test-not-a-secret";

    private IContainer _container = null!;

    public Uri Endpoint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var pin = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Aws", "emulator.json")));
        string image = $"{pin.RootElement.GetProperty("image").GetString()}@{pin.RootElement.GetProperty("digest").GetString()}";
        int port = pin.RootElement.GetProperty("port").GetInt32();
        _container = new ContainerBuilder(image)
            .WithPortBinding(port, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort((ushort)port).ForPath("/_localstack/health")))
            .Build();
        await _container.StartAsync();
        Endpoint = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(port)}");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AmazonSQSClient Sqs() => new(new BasicAWSCredentials(Account, Secret), new AmazonSQSConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });

    public AmazonEventBridgeClient Events() => new(new BasicAWSCredentials(Account, Secret), new AmazonEventBridgeConfig { ServiceURL = Endpoint.ToString(), AuthenticationRegion = Region });
}
