using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sentinel.AppHost;

/// <summary>
/// Around the AWS-emulated environment's lifetime: creates the Docker network the emulator starts its ECS tasks on, and
/// removes every container the emulator started, by the label it gives them, when the AppHost starts and when it stops,
/// because the emulator does not remove them itself (ADR-021). The emulator itself is Aspire's and never carries the label.
/// </summary>
internal sealed class AwsEnvironmentLifecycle(ILogger<AwsEnvironmentLifecycle> logger) : IHostedService
{
    public const string Network = "reference-aws";
    public const string LabelKey = "sentinel.reference.emulator";
    public const string LabelValue = "apphost";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await RemoveStartedContainersAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(await DockerAsync(cancellationToken, "network", "ls", "-q", "--filter", $"name=^{Network}$")))
        {
            await DockerAsync(cancellationToken, "network", "create", "--label", $"{LabelKey}={LabelValue}", Network);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => RemoveStartedContainersAsync(cancellationToken);

    private async Task RemoveStartedContainersAsync(CancellationToken cancellationToken)
    {
        string started = await DockerAsync(cancellationToken, "ps", "-a", "-q", "--filter", $"label={LabelKey}={LabelValue}");
        foreach (string container in started.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            logger.LogInformation("Removing container {Container}, which the AWS emulator started", container);
            await DockerAsync(cancellationToken, "rm", "-f", container);
        }
    }

    private static async Task<string> DockerAsync(CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process docker = Process.Start(start)!;
        Task<string> error = docker.StandardError.ReadToEndAsync(cancellationToken);
        string output = await docker.StandardOutput.ReadToEndAsync(cancellationToken);
        await docker.WaitForExitAsync(cancellationToken);
        return docker.ExitCode == 0
            ? output
            : throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed: {await error}");
    }
}
