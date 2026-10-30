using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The SQS queue-attribute metrics provider against the pinned emulator (Phase E01, Stage 2): a series adds up the
/// attributes its expression names, as one sample at the step of the reading, for the service the series names; a pull
/// within the same step reads the same observation again. A malformed expression, a series without a service and a queue
/// that does not exist are unavailable results, never an exception or a quiet queue.
/// </summary>
[Collection(AwsEmulatorCollection.Name)]
[Trait("Category", "Emulator")]
public sealed class SqsQueueMetricsProviderTests(AwsEmulator emulator)
{
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_series_adds_up_the_attributes_it_names_as_one_sample_at_the_step_of_the_reading()
    {
        using AmazonSQSClient sqs = emulator.Sqs();
        string queue = $"orders-{Guid.NewGuid().ToString("N")[..8]}";
        string url = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = queue })).QueueUrl;
        for (int order = 0; order < 3; order++)
        {
            await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = url, MessageBody = $"order-{order}" });
        }

        (await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = url, MaxNumberOfMessages = 1, VisibilityTimeout = 60 })).Messages.ShouldHaveSingleItem();
        SqsQueueMetricsProvider provider = Provider(sqs);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        ProviderResult<MetricObservation> depth = await provider.QueryRangeAsync(Request(Series("queue_depth", $"{queue}:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible")), CancellationToken.None);
        ProviderResult<MetricObservation> inFlight = await provider.QueryRangeAsync(Request(Series("queue_in_flight", $"{queue}:ApproximateNumberOfMessagesNotVisible")), CancellationToken.None);

        depth.Status.ShouldBe(ProviderStatus.Healthy, depth.Error);
        MetricObservation sample = depth.Observations.ShouldHaveSingleItem();
        sample.Value.ShouldBe(3, "two waiting and one in flight");
        sample.ServiceId.ShouldBe(ServiceId.From("queue-consumer"));
        sample.MetricName.ShouldBe("queue_depth");
        sample.SourceProvider.ShouldBe(SqsQueueMetricsProvider.ProviderName);
        sample.Labels["queue"].ShouldBe(queue);
        sample.EventTime.ShouldBeGreaterThan(before - Step);
        (sample.EventTime.ToUnixTimeMilliseconds() % (long)Step.TotalMilliseconds).ShouldBe(0, "a sample sits on a step");
        inFlight.Observations.ShouldHaveSingleItem().Value.ShouldBe(1);

        ProviderResult<MetricObservation> again = await provider.QueryRangeAsync(Request(Series("queue_depth", $"{queue}:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible")), CancellationToken.None);
        if (again.Observations.Single().EventTime == sample.EventTime)
        {
            again.Observations.Single().ObservationId.ShouldBe(sample.ObservationId, "a reading in the same step is the same observation");
        }
    }

    [Theory]
    [InlineData("queue_depth", "orders", "queue-consumer", "must name a queue and its attributes")]
    [InlineData("queue_depth", "orders:ApproximateNumberOfMessages", null, "must name its service")]
    [InlineData("queue_depth", "no-such-queue:ApproximateNumberOfMessages", "queue-consumer", "")]
    public async Task A_series_it_cannot_read_is_an_unavailable_result(string name, string expression, string? service, string reason)
    {
        using AmazonSQSClient sqs = emulator.Sqs();

        ProviderResult<MetricObservation> result = await Provider(sqs).QueryRangeAsync(Request(Series(name, expression) with { Service = service }), CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Unavailable);
        result.Observations.ShouldBeEmpty();
        result.Error.ShouldNotBeNull().ShouldContain(reason);
    }

    private static SqsQueueMetricsProvider Provider(IAmazonSQS sqs) => new(sqs, TimeProvider.System, NullLogger<SqsQueueMetricsProvider>.Instance);

    private static MetricSeriesDefinition Series(string name, string expression) => new()
    {
        Name = name,
        Expression = expression,
        Service = "queue-consumer",
        Unit = "messages",
        Aggregation = "gauge",
        Provider = SqsQueueMetricsProvider.ProviderName
    };

    private static MetricRangeRequest Request(MetricSeriesDefinition series)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var to = new DateTimeOffset(now.UtcTicks - now.UtcTicks % Step.Ticks, TimeSpan.Zero);
        return new MetricRangeRequest(Tenant, Environment, series, new TimeRange(to - Step, to), Step);
    }
}
