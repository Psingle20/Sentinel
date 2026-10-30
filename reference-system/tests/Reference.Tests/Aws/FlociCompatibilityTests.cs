using System.Diagnostics;
using System.Text.Json;
using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using Amazon.Runtime;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Shouldly;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Reference.Tests.Aws;

/// <summary>
/// The compatibility gate of the AWS-emulated environment proposal, against the emulator ADR-021 pins: each test is one
/// gate check with the AWS SDK clients the scenario runner and Sentinel's adapters will use, so the operation matrix is
/// these tests' results. The checks assert what the first scenarios need; a failure is an incompatibility to record.
/// </summary>
[Trait("Category", "Emulator")]
[Collection(EmulatorCollection.Name)]
public sealed class FlociCompatibilityTests(FlociEmulator emulator, ITestOutputHelper output) : IClassFixture<FlociEmulator>
{
    [Fact]
    public async Task Starts_from_the_pinned_image_and_reports_the_services_the_scenarios_use()
    {
        using JsonDocument health = await emulator.HealthAsync();
        JsonElement services = health.RootElement.GetProperty("services");
        output.WriteLine($"Started in {emulator.StartedIn.TotalSeconds:F1} s; {health.RootElement}");

        // CloudWatch metrics report as "monitoring"; STS is served but not listed, so its own test proves it.
        foreach (string service in new[] { "sqs", "ecs", "iam", "logs", "monitoring", "events" })
        {
            services.TryGetProperty(service, out JsonElement state).ShouldBeTrue($"{service} is served");
            state.GetString().ShouldBe("running");
        }

        health.RootElement.GetProperty("version").GetString().ShouldBe(FlociEmulator.Pin.Version);
    }

    [Fact]
    public async Task A_poison_message_moves_to_the_dead_letter_queue_after_its_receive_limit()
    {
        using AmazonSQSClient sqs = emulator.Sqs();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string deadLetters = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"orders-dlq-{suffix}" })).QueueUrl;
        string deadLetterArn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = deadLetters, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];
        string queue = (await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = $"orders-{suffix}",
            Attributes = new Dictionary<string, string>
            {
                ["VisibilityTimeout"] = "1",
                ["RedrivePolicy"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["deadLetterTargetArn"] = deadLetterArn, ["maxReceiveCount"] = "2" })
            }
        })).QueueUrl;

        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = "{\"orderId\":\"poison\"}" });

        // A consumer that keeps failing never deletes the message; after two receives it belongs to the dead-letter queue.
        int receives = 0;
        Message? moved = null;
        var deadline = Stopwatch.StartNew();
        while (moved is null && deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            receives += (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, WaitTimeSeconds = 1, MaxNumberOfMessages = 1 })).Messages?.Count ?? 0;
            await Task.Delay(TimeSpan.FromSeconds(1.2));
            moved = (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = deadLetters, WaitTimeSeconds = 1, MaxNumberOfMessages = 1 })).Messages?.SingleOrDefault();
        }

        output.WriteLine($"Moved after {receives} receives and {deadline.Elapsed.TotalSeconds:F1} s.");
        moved.ShouldNotBeNull("the message reached the dead-letter queue").Body.ShouldContain("poison");
        receives.ShouldBe(2, "the redrive policy's receive limit decides when it moves");
        Dictionary<string, string> depth = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = queue, AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] })).Attributes;
        depth["ApproximateNumberOfMessages"].ShouldBe("0");
    }

    [Fact]
    public async Task A_redrive_policy_set_later_applies_only_to_a_queue_created_without_one()
    {
        // A known incompatibility (ADR-021). SQS applies a changed redrive policy at the next receive; the emulator applies
        // one set later only to a queue created without a policy, and otherwise keeps applying the policy the queue was
        // created with while reporting the new one. The quarantine adapter lowers the policy for a window, so the AWS
        // environment's queue has none of its own.
        using AmazonSQSClient sqs = emulator.Sqs();
        string suffix = Guid.NewGuid().ToString("N")[..8];

        (string bare, string bareDeadLetters, string bareArn) = await QueueAsync(sqs, $"bare-{suffix}", maxReceiveCount: null);
        (string created, string createdDeadLetters, string createdArn) = await QueueAsync(sqs, $"created-{suffix}", maxReceiveCount: "100");
        int bareMoved = await LowerAndReceiveAsync(sqs, bare, bareDeadLetters, bareArn);
        int createdMoved = await LowerAndReceiveAsync(sqs, created, createdDeadLetters, createdArn);

        bareMoved.ShouldBe(1, "a policy set on a queue created without one applies at the next receive");
        createdMoved.ShouldBe(0, "the policy a queue was created with still applies");
        JsonDocument.Parse((await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = created, AttributeNames = ["RedrivePolicy"] })).Attributes["RedrivePolicy"])
            .RootElement.GetProperty("maxReceiveCount").ToString().ShouldBe("2", "the new policy is reported nonetheless");
    }

    private static async Task<(string Queue, string DeadLetters, string DeadLetterArn)> QueueAsync(AmazonSQSClient sqs, string name, string? maxReceiveCount)
    {
        string deadLetters = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = $"{name}-dlq" })).QueueUrl;
        string arn = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = deadLetters, AttributeNames = ["QueueArn"] })).Attributes["QueueArn"];
        var request = new CreateQueueRequest { QueueName = name };
        if (maxReceiveCount is not null)
        {
            request.Attributes = new Dictionary<string, string> { ["RedrivePolicy"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["deadLetterTargetArn"] = arn, ["maxReceiveCount"] = maxReceiveCount }) };
        }

        return ((await sqs.CreateQueueAsync(request)).QueueUrl, deadLetters, arn);
    }

    // Three failed receives, a policy that dead-letters after two, then one more receive: how many messages it moved.
    private static async Task<int> LowerAndReceiveAsync(AmazonSQSClient sqs, string queue, string deadLetters, string deadLetterArn)
    {
        await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = "{\"orderId\":\"poison\"}" });
        for (int receive = 0; receive < 4; receive++)
        {
            if (receive == 3)
            {
                await sqs.SetQueueAttributesAsync(new SetQueueAttributesRequest
                {
                    QueueUrl = queue,
                    Attributes = new Dictionary<string, string> { ["RedrivePolicy"] = JsonSerializer.Serialize(new Dictionary<string, string> { ["deadLetterTargetArn"] = deadLetterArn, ["maxReceiveCount"] = "2" }) }
                });
            }

            foreach (Message message in (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = queue, WaitTimeSeconds = 1, MaxNumberOfMessages = 1 })).Messages ?? [])
            {
                await sqs.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest { QueueUrl = queue, ReceiptHandle = message.ReceiptHandle, VisibilityTimeout = 0 });
            }
        }

        return int.Parse((await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = deadLetters, AttributeNames = ["ApproximateNumberOfMessages"] })).Attributes["ApproximateNumberOfMessages"], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task An_ecs_service_runs_its_task_as_a_real_container_and_rolls_out_a_new_revision()
    {
        using AmazonECSClient ecs = emulator.Ecs();
        string cluster = $"reference-{Guid.NewGuid():N}"[..20];
        await ecs.CreateClusterAsync(new CreateClusterRequest { ClusterName = cluster });
        string first = await EcsWorkload.RegisterAsync(ecs, "1");
        await ecs.CreateServiceAsync(new CreateServiceRequest { Cluster = cluster, ServiceName = EcsWorkload.Service, TaskDefinition = first, DesiredCount = 1 });

        Service running = await EcsWorkload.WaitForAsync(ecs, cluster, service => service.RunningCount == 1 && service.Deployments?.Single().RolloutState == DeploymentRolloutState.COMPLETED);
        running.TaskDefinition.ShouldBe(first);
        (await emulator.StartedContainersAsync()).ShouldNotBeEmpty("the task runs as a real container");

        string second = await EcsWorkload.RegisterAsync(ecs, "2");
        await ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = cluster, Service = EcsWorkload.Service, TaskDefinition = second });
        Service rolled = await EcsWorkload.WaitForAsync(ecs, cluster, service => service.TaskDefinition == second && service.RunningCount == 1 && service.Deployments?.Single().RolloutState == DeploymentRolloutState.COMPLETED);

        // Under load the service reports the rollout complete before the task list shows the new task (ADR-021), so the
        // replacement is awaited on the tasks themselves.
        var replaced = Stopwatch.StartNew();
        List<Amazon.ECS.Model.Task> described = [];
        while (replaced.Elapsed < TimeSpan.FromSeconds(60))
        {
            List<string> tasks = (await ecs.ListTasksAsync(new ListTasksRequest { Cluster = cluster, ServiceName = EcsWorkload.Service })).TaskArns ?? [];
            described = (await ecs.DescribeTasksAsync(new DescribeTasksRequest { Cluster = cluster, Tasks = tasks })).Tasks ?? [];
            if (described.Count > 0 && described.TrueForAll(task => task.TaskDefinitionArn == second))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        described.ShouldAllBe(task => task.TaskDefinitionArn == second, "the rollout replaced the first revision's task");
        output.WriteLine($"Rolled out {rolled.TaskDefinition}; {described.Count} task(s) on it, {replaced.Elapsed.TotalSeconds:F1} s after the service said so.");

        await ecs.UpdateServiceAsync(new UpdateServiceRequest { Cluster = cluster, Service = EcsWorkload.Service, DesiredCount = 0 });
        await EcsWorkload.WaitForAsync(ecs, cluster, service => service.RunningCount == 0);
        await ecs.DeleteServiceAsync(new DeleteServiceRequest { Cluster = cluster, Service = EcsWorkload.Service });
    }

    [Fact]
    public async Task Sts_resolves_the_account_from_the_access_key_and_an_assumed_role_acts_as_that_role()
    {
        const string account = "111111111111";
        using AmazonSecurityTokenServiceClient sts = emulator.Sts(account);
        (await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest())).Account.ShouldBe(account);

        using AmazonIdentityManagementServiceClient iam = emulator.Iam(account);
        string trust = JsonSerializer.Serialize(new
        {
            Version = "2012-10-17",
            Statement = new[] { new { Effect = "Allow", Principal = new { AWS = $"arn:aws:iam::{account}:root" }, Action = "sts:AssumeRole" } }
        });
        string role = (await iam.CreateRoleAsync(new CreateRoleRequest { RoleName = $"executor-{Guid.NewGuid():N}"[..20], AssumeRolePolicyDocument = trust })).Role.Arn;

        Credentials assumed = (await sts.AssumeRoleAsync(new AssumeRoleRequest { RoleArn = role, RoleSessionName = "quarantine" })).Credentials;
        using AmazonSecurityTokenServiceClient asRole = emulator.Sts(new SessionAWSCredentials(assumed.AccessKeyId, assumed.SecretAccessKey, assumed.SessionToken));
        GetCallerIdentityResponse identity = await asRole.GetCallerIdentityAsync(new GetCallerIdentityRequest());

        output.WriteLine($"Assumed {identity.Arn} in {identity.Account}.");
        identity.Account.ShouldBe(account);
        identity.Arn.ShouldContain(":assumed-role/");
    }

    [Fact]
    public async Task Queues_of_two_accounts_and_of_two_regions_are_invisible_to_each_other()
    {
        string name = $"orders-{Guid.NewGuid():N}"[..20];
        using AmazonSQSClient first = emulator.Sqs("111111111111");
        using AmazonSQSClient second = emulator.Sqs("222222222222");
        using AmazonSQSClient otherRegion = emulator.Sqs("111111111111", "eu-west-1");

        string url = (await first.CreateQueueAsync(new CreateQueueRequest { QueueName = name })).QueueUrl;

        ((await second.ListQueuesAsync(new ListQueuesRequest { QueueNamePrefix = name })).QueueUrls ?? []).ShouldBeEmpty("another account does not see the queue");
        ((await otherRegion.ListQueuesAsync(new ListQueuesRequest { QueueNamePrefix = name })).QueueUrls ?? []).ShouldBeEmpty("another region does not see the queue");
        string secondUrl = (await second.CreateQueueAsync(new CreateQueueRequest { QueueName = name })).QueueUrl;
        output.WriteLine($"{url} and {secondUrl}");
        secondUrl.ShouldNotBe(url);
    }

    [Fact]
    public async Task Log_events_and_metric_datapoints_are_written_and_read_back()
    {
        using AmazonCloudWatchLogsClient logs = emulator.CloudWatchLogs();
        string group = $"/ecs/queue-consumer-{Guid.NewGuid():N}"[..30];
        await logs.CreateLogGroupAsync(new CreateLogGroupRequest { LogGroupName = group });
        await logs.CreateLogStreamAsync(new CreateLogStreamRequest { LogGroupName = group, LogStreamName = "task-1" });
        await logs.PutLogEventsAsync(new PutLogEventsRequest
        {
            LogGroupName = group,
            LogStreamName = "task-1",
            LogEvents = [new InputLogEvent { Message = "Failed to process message orderId=poison", Timestamp = DateTime.UtcNow }]
        });
        List<OutputLogEvent> events = (await logs.GetLogEventsAsync(new GetLogEventsRequest { LogGroupName = group, LogStreamName = "task-1", StartFromHead = true })).Events ?? [];
        events.ShouldContain(logEvent => logEvent.Message.Contains("orderId=poison"));

        using AmazonCloudWatchClient metrics = emulator.Metrics();
        Dimension queue = new() { Name = "QueueName", Value = "orders" };
        DateTime now = DateTime.UtcNow;
        await metrics.PutMetricDataAsync(new PutMetricDataRequest
        {
            Namespace = "Reference/Queue",
            MetricData = [new MetricDatum { MetricName = "ProcessingFailures", Value = 3, Unit = Amazon.CloudWatch.StandardUnit.Count, Timestamp = now, Dimensions = [queue] }]
        });
        List<Datapoint> datapoints = (await metrics.GetMetricStatisticsAsync(new GetMetricStatisticsRequest
        {
            Namespace = "Reference/Queue",
            MetricName = "ProcessingFailures",
            Dimensions = [queue],
            StartTime = now.AddMinutes(-5),
            EndTime = now.AddMinutes(5),
            Period = 60,
            Statistics = ["Sum"]
        })).Datapoints ?? [];
        output.WriteLine($"{events.Count} log event(s); {datapoints.Count} datapoint(s).");
        datapoints.Sum(datapoint => datapoint.Sum ?? 0).ShouldBe(3);
    }
}
