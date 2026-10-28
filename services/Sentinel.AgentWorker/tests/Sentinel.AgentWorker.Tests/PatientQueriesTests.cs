using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

public sealed class PatientQueriesTests
{
    private static readonly DateTimeOffset Now = Telemetry.Start.AddMinutes(5);

    [Fact]
    public async Task A_merely_late_answer_is_asked_again_until_the_sources_catch_up()
    {
        var time = new FakeTimeProvider(Now);
        var inner = new LaggingQueries(time, catchesUpAfter: TimeSpan.FromSeconds(6));
        var queries = new PatientObservationQueries(inner, Options.Create(new CompletenessOptions { MaxWait = TimeSpan.FromSeconds(30), PollInterval = TimeSpan.FromSeconds(3) }), time);

        Task<ObservationQueryResult> pending = queries.QueryAsync(Query(), CancellationToken.None);
        for (int tick = 0; tick < 4 && !pending.IsCompleted; tick++)
        {
            await Task.Yield();
            time.Advance(TimeSpan.FromSeconds(3));
        }

        ObservationQueryResult result = await pending;
        result.Metadata.ObservedThrough.ShouldBe(Now);
        inner.Calls.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task An_unhealthy_answer_is_returned_at_once_as_partial()
    {
        var time = new FakeTimeProvider(Now);
        var inner = new LaggingQueries(time, catchesUpAfter: TimeSpan.FromMinutes(5), completeness: QueryCompleteness.Partial);
        var queries = new PatientObservationQueries(inner, Options.Create(new CompletenessOptions()), time);

        ObservationQueryResult result = await queries.QueryAsync(Query(), CancellationToken.None);

        result.Metadata.Completeness.ShouldBe(QueryCompleteness.Partial);
        inner.Calls.ShouldBe(1);
    }

    private static ObservationQuery Query() => new()
    {
        TenantId = Telemetry.Tenant,
        EnvironmentId = Telemetry.Environment,
        Kind = ObservationKind.Log,
        Range = new TimeRange(Now.AddMinutes(-15), Now)
    };

    /// <summary>A store whose watermark trails now until <c>catchesUpAfter</c> has passed.</summary>
    private sealed class LaggingQueries(TimeProvider time, TimeSpan catchesUpAfter, QueryCompleteness completeness = QueryCompleteness.Complete) : IObservationQueries
    {
        private readonly DateTimeOffset _caughtUp = time.GetUtcNow() + catchesUpAfter;

        public int Calls { get; private set; }

        public Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            DateTimeOffset through = time.GetUtcNow() >= _caughtUp ? query.Range.To : query.Range.To.AddSeconds(-20);
            return Task.FromResult(new ObservationQueryResult([], new QueryMetadata
            {
                ObservedThrough = through,
                Freshness = TimeSpan.Zero,
                Completeness = completeness,
                Truncated = false,
                ProviderHealth = [],
                RedactionPolicyVersion = "test"
            }));
        }
    }
}
