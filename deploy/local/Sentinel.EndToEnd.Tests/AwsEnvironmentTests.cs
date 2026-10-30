using System.Net.Http.Json;
using System.Text.Json;
using Reference.Scenarios.Application.Runs;
using Shouldly;
using Xunit.Abstractions;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Phase E01, Stage 1, through the AppHost with <c>Sentinel:ReferenceEnvironment=aws</c> and Sentinel not running: the
/// runner provisions the queues and the queue-consumer's ECS service in the pinned emulator, the consumer's telemetry
/// reaches the same Prometheus as the local workloads', <c>AWS-S0</c> rolls a revision out through ECS without the queue
/// ageing, and a reset leaves the environment ready on its baseline.
/// </summary>
[Collection(AwsReferenceCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class AwsEnvironmentTests(AwsReferenceOnlySystem system, ITestOutputHelper output)
{
    [Fact]
    public async Task AWS_S0_runs_from_a_clean_reset_with_the_consumer_on_ecs_reporting_like_the_local_one()
    {
        ScenarioRunView run = await system.StartScenarioAsync("AWS-S0");
        try
        {
            JsonElement environment = await system.ScenarioRunner.GetFromJsonAsync<JsonElement>("/api/environment");
            output.WriteLine($"Environment: {environment}");
            environment.GetProperty("environment").GetString().ShouldBe("aws");
            environment.GetProperty("ready").GetBoolean().ShouldBeTrue(environment.GetProperty("problems").ToString());
            environment.GetProperty("emulator").GetString().ShouldStartWith("floci ");
            environment.GetProperty("imageDigest").GetString().ShouldStartWith("sha256:");

            double processed = await Eventually.ValueAsync(
                async () => await system.Prometheus.ScalarAsync("sum(rate(reference_queue_messages_total{outcome=\"processed\"}[1m]))") ?? 0,
                value => value > 2,
                TimeSpan.FromMinutes(2),
                "the ECS-hosted consumer's processed orders reach Prometheus");
            output.WriteLine($"The ECS-hosted consumer processes {processed:F1} orders a second.");

            ScenarioRunView finished = await system.WaitForRunToFinishAsync(run.RunId, TimeSpan.FromSeconds(system.Scenarios.Manifests["AWS-S0"].DurationSeconds + 60));
            finished.Phase.ShouldBe("completed");
            finished.Steps.ShouldAllBe(step => step.Error == null, "the revision rolled out through ECS");

            double? oldest = await system.Prometheus.ScalarAsync("max_over_time(max(reference_queue_oldest_message_age_seconds)[3m:5s])");
            output.WriteLine($"The oldest message was at most {oldest:F1} s old during the run.");
            oldest.ShouldNotBeNull("the ECS-hosted consumer reports its queue's oldest message").ShouldBeLessThan(10, "the rollout does not age the queue");
        }
        catch
        {
            await system.ReportAsync(output, run.RunId);
            throw;
        }
        finally
        {
            await system.StopScenarioAsync(run.RunId);
        }

        using HttpResponseMessage reset = await system.ScenarioRunner.PostAsync(new Uri("/api/reset", UriKind.Relative), null);
        reset.EnsureSuccessStatusCode();
        (await system.ScenarioRunner.GetFromJsonAsync<JsonElement>("/api/environment")).GetProperty("ready").GetBoolean().ShouldBeTrue("a reset leaves the environment ready on its baseline");
    }
}
