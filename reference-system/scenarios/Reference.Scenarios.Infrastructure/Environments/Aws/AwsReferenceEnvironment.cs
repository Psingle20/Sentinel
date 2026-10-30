using System.Text.Json;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Domain.Scenarios;
using Reference.Scenarios.Infrastructure.Environments.Local;
using Task = System.Threading.Tasks.Task;

namespace Reference.Scenarios.Infrastructure.Environments.Aws;

/// <summary>
/// The AWS-emulated reference environment (Phase E01, Stage 1): the queue-consumer runs as an ECS service on an SQS queue
/// with a dead-letter queue, in the emulator ADR-021 pins, beside the local workloads, which the local environment still
/// drives. Provisioning and inspection use the public SQS and ECS APIs Sentinel's adapters will use, never an emulator's
/// own. A reset scales the consumer to zero, empties both queues, proves them empty and starts the baseline revision
/// again, so no message or booking of an earlier run survives it. The consumer's fault controls run inside its task,
/// which the runner does not reach, so a step for it is one an AWS operator could take: a deployment through ECS, a
/// message through SQS.
/// </summary>
public sealed class AwsReferenceEnvironment(
    IAmazonSQS sqs,
    IAmazonECS ecs,
    AwsEnvironmentOptions options,
    AwsProvisioning provisioning,
    LocalReferenceEnvironment local,
    TimeProvider time,
    ILogger<AwsReferenceEnvironment> logger) : IReferenceEnvironment
{
    public const string EnvironmentName = "aws";
    public const string ProvisioningVersion = "1";

    // High enough never to move a message on its own: quarantine is an action, not a side effect of the queue.
    private const string MaxReceiveCount = "100";

    private static readonly TimeSpan RolloutTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => EnvironmentName;

    public async Task ProvisionAsync(CancellationToken cancellationToken)
    {
        await provisioning.Gate.WaitAsync(cancellationToken);
        try
        {
            if (provisioning.Done)
            {
                return;
            }

            await CallAsync(async () =>
            {
                string deadLetters = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = options.DeadLetterQueue }, cancellationToken)).QueueUrl;
                string deadLetterArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = deadLetters, AttributeNames = ["QueueArn"] }, cancellationToken)).Attributes["QueueArn"];
                await sqs.CreateQueueAsync(
                    new CreateQueueRequest
                    {
                        QueueName = options.Queue,
                        Attributes = new Dictionary<string, string>
                        {
                            ["VisibilityTimeout"] = "30",
                            ["RedrivePolicy"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["deadLetterTargetArn"] = deadLetterArn, ["maxReceiveCount"] = MaxReceiveCount })
                        }
                    },
                    cancellationToken);

                await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = options.Cluster }, cancellationToken);
                foreach (string revision in options.Revisions)
                {
                    provisioning.TaskDefinitions[revision] = (await ecs.RegisterTaskDefinitionAsync(ConsumerTask.Definition(options, revision), cancellationToken)).TaskDefinition.TaskDefinitionArn;
                }

                if (await ServiceAsync(cancellationToken) is not { Status: "ACTIVE" })
                {
                    await ecs.CreateServiceAsync(
                        new CreateServiceRequest { Cluster = options.Cluster, ServiceName = options.Service, TaskDefinition = provisioning.TaskDefinitions[options.BaselineRevision], DesiredCount = 1 },
                        cancellationToken);
                }
            });

            provisioning.Done = true;
            logger.LogInformation("Provisioned the AWS environment: queues {Queue} and {DeadLetterQueue}, ECS service {Service} on {Image}", options.Queue, options.DeadLetterQueue, options.Service, options.Image);
        }
        finally
        {
            provisioning.Gate.Release();
        }
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await local.ResetAsync(cancellationToken);
        await ProvisionAsync(cancellationToken);
        string baseline = provisioning.TaskDefinitions[options.BaselineRevision];

        await CallAsync(() => ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = options.Cluster, Service = options.Service, TaskDefinition = baseline, DesiredCount = 0 }, cancellationToken));
        await WaitForAsync(service => service.RunningCount == 0, "stops its tasks", cancellationToken);

        await CallAsync(async () =>
        {
            foreach (string queue in new[] { options.Queue, options.DeadLetterQueue })
            {
                await sqs.PurgeQueueAsync(await QueueUrlAsync(queue, cancellationToken), cancellationToken);
            }
        });
        foreach (string queue in new[] { options.Queue, options.DeadLetterQueue })
        {
            if (await DepthAsync(queue, cancellationToken) is > 0 and int left)
            {
                throw new ReferenceEnvironmentException(EnvironmentFailure.PartialReset, $"{left} message(s) of an earlier run are still in {queue} after the purge.");
            }
        }

        await CallAsync(() => ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = options.Cluster, Service = options.Service, TaskDefinition = baseline, DesiredCount = 1 }, cancellationToken));
        await WaitForAsync(service => service.RunningCount == 1 && service.TaskDefinition == baseline, $"serves {options.BaselineRevision} again", cancellationToken);
    }

    public async Task ActivateAsync(ScenarioStep step, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.Service != options.Service)
        {
            await local.ActivateAsync(step, runId, cancellationToken);
            return;
        }

        switch (step.Action)
        {
            case "deploy" when step.Version is not null && provisioning.TaskDefinitions.TryGetValue(step.Version, out string? revision):
                await CallAsync(() => ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = options.Cluster, Service = options.Service, TaskDefinition = revision }, cancellationToken));
                break;
            case "deploy":
                throw new ReferenceEnvironmentException(EnvironmentFailure.Unsupported, $"{options.Service} has no revision {step.Version}.");
            case "poisonMessage":
                DateTimeOffset now = time.GetUtcNow();
                string body = JsonSerializer.Serialize(new { id = $"msg-poison-{now.ToUnixTimeMilliseconds()}", orderId = $"ord-poison-{now.ToUnixTimeMilliseconds()}", amount = "NaN", enqueuedAt = now }, Json);
                await CallAsync(async () => await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = await QueueUrlAsync(options.Queue, cancellationToken), MessageBody = body }, cancellationToken));
                break;
            default:
                throw new ReferenceEnvironmentException(
                    EnvironmentFailure.Unsupported,
                    $"The AWS environment cannot {step.Action} {step.Service}: that fault control runs inside the ECS task, which the runner does not reach.");
        }
    }

    public async Task<EnvironmentState> InspectAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        try
        {
            string[] queues = (await sqs.ListQueuesAsync(new ListQueuesRequest(), cancellationToken)).QueueUrls?.ToArray() ?? [];
            foreach (string queue in new[] { options.Queue, options.DeadLetterQueue }.Where(name => !queues.Any(url => url.EndsWith($"/{name}", StringComparison.Ordinal))))
            {
                problems.Add($"The queue {queue} does not exist.");
            }

            switch (await ServiceAsync(cancellationToken))
            {
                case null or { Status: not "ACTIVE" }:
                    problems.Add($"The ECS service {options.Service} does not exist.");
                    break;
                case { RunningCount: var running, DesiredCount: var desired } when running != desired || desired == 0:
                    problems.Add($"The ECS service {options.Service} runs {running} of {desired} task(s).");
                    break;
            }
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            problems.Add($"The emulator did not answer: {exception.Message}");
        }

        return new EnvironmentState(Name, problems.Count == 0, problems, options.Emulator, options.ImageDigest, ProvisioningVersion);
    }

    private async Task<Service?> ServiceAsync(CancellationToken cancellationToken) =>
        (await ecs.DescribeServicesAsync(new DescribeServicesRequest { Cluster = options.Cluster, Services = [options.Service] }, cancellationToken)).Services?.FirstOrDefault();

    private async Task<string> QueueUrlAsync(string queue, CancellationToken cancellationToken) =>
        (await sqs.GetQueueUrlAsync(queue, cancellationToken)).QueueUrl;

    private async Task<int> DepthAsync(string queue, CancellationToken cancellationToken) => await CallAsync(async () =>
    {
        Dictionary<string, string> attributes = (await sqs.GetQueueAttributesAsync(
            new GetQueueAttributesRequest { QueueUrl = await QueueUrlAsync(queue, cancellationToken), AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] },
            cancellationToken)).Attributes ?? [];
        return attributes.Values.Sum(value => int.TryParse(value, out int count) ? count : 0);
    });

    private async Task WaitForAsync(Func<Service, bool> condition, string what, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = time.GetUtcNow() + RolloutTimeout;
        while (true)
        {
            Service? service = await CallAsync(() => ServiceAsync(cancellationToken));
            if (service is not null && condition(service))
            {
                return;
            }

            if (time.GetUtcNow() > deadline)
            {
                throw new ReferenceEnvironmentException(
                    EnvironmentFailure.Timeout,
                    $"{options.Service} did not get to where it {what} within {RolloutTimeout.TotalSeconds:F0} s: running {service?.RunningCount}, desired {service?.DesiredCount}, revision {service?.TaskDefinition}.");
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    private static async Task CallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new ReferenceEnvironmentException(EnvironmentFailure.Unavailable, $"The emulator refused or did not answer: {exception.Message}");
        }
    }

    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            throw new ReferenceEnvironmentException(EnvironmentFailure.Unavailable, $"The emulator refused or did not answer: {exception.Message}");
        }
    }
}
