using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Shouldly;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Reference.Tests.Aws;

/// <summary>
/// Gate check 9 of the compatibility gate: local AWS runs and serves SQS with no way out of the machine, so it needs
/// no external credentials, uploads no telemetry and calls no paid service. It runs on an internal Docker network, which
/// has no route outside, and a probe container on the same network, an image already local, drives it over the AWS JSON
/// protocol the SDK uses. Local AWS does not check signatures, so the probe sends none.
/// </summary>
[Trait("Category", "Aws")]
[Collection(LocalAwsCollection.Name)]
public sealed class FlociOfflineTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Local_aws_starts_and_serves_sqs_on_a_network_without_outbound_access()
    {
        INetwork network = new NetworkBuilder()
            .WithName($"reference-aws-offline-{Guid.NewGuid().ToString("N")[..8]}")
            .WithCreateParameterModifier(parameters => parameters.Internal = true)
            .Build();
        await network.CreateAsync();
        IContainer localAws = new ContainerBuilder($"{LocalAws.Pin.Image}@{LocalAws.Pin.Digest}")
            .WithNetwork(network)
            .WithNetworkAliases("local-aws")
            .WithEnvironment("FLOCI_HOSTNAME", "local-aws")
            .Build();
        IContainer probe = new ContainerBuilder("redis:7-alpine")
            .WithNetwork(network)
            .WithCommand("sleep", "300")
            .Build();
        try
        {
            var started = Stopwatch.StartNew();
            await localAws.StartAsync();
            await probe.StartAsync();

            string health = await UntilAsync(() => ExecAsync(probe, "wget", "-qO-", "http://local-aws:4566/_localstack/health"));
            output.WriteLine($"Healthy without outbound access after {started.Elapsed.TotalSeconds:F1} s: {health}");
            health.ShouldContain("sqs");

            // No default route: nothing on this network can leave the machine.
            string routes = (await ExecAsync(probe, "cat", "/proc/net/route")).ShouldNotBeNull();
            routes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .ShouldNotContain(route => route.Split('\t', StringSplitOptions.RemoveEmptyEntries)[1] == "00000000", "the network has no route outside");

            string queue = (await SqsAsync(probe, "CreateQueue", """{"QueueName":"offline"}""")).ShouldNotBeNull();
            queue.ShouldContain("offline");
            (await SqsAsync(probe, "SendMessage", """{"QueueUrl":"http://local-aws:4566/000000000000/offline","MessageBody":"no way out"}""")).ShouldNotBeNull();
            string received = (await SqsAsync(probe, "ReceiveMessage", """{"QueueUrl":"http://local-aws:4566/000000000000/offline","MaxNumberOfMessages":1}""")).ShouldNotBeNull();
            received.ShouldContain("no way out");
        }
        finally
        {
            await probe.DisposeAsync();
            await localAws.DisposeAsync();
            await network.DisposeAsync();
        }
    }

    private static Task<string?> SqsAsync(IContainer probe, string action, string body) => ExecAsync(
        probe,
        "wget", "-qO-",
        "--header", $"X-Amz-Target: AmazonSQS.{action}",
        "--header", "Content-Type: application/x-amz-json-1.0",
        "--post-data", body,
        "http://local-aws:4566/");

    /// <summary>The command's output, or null when it failed.</summary>
    private static async Task<string?> ExecAsync(IContainer container, params string[] command)
    {
        ExecResult result = await container.ExecAsync(command);
        return result.ExitCode == 0 ? result.Stdout : null;
    }

    private static async Task<string> UntilAsync(Func<Task<string?>> attempt)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (await attempt() is { } result)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("Local AWS did not answer on the internal network within 60 s.");
    }
}
