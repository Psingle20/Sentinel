using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Api;
using Sentinel.Gateway.Api.Filters;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;
using Sentinel.Gateway.Api.Identity;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Options;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Live;

namespace Sentinel.Gateway.Tests;

public sealed class LiveUpdateTests
{
    private static readonly TenantId Acme = TenantId.From("acme");
    private static readonly TenantId Globex = TenantId.From("globex");

    [Fact]
    public async Task A_reconnecting_client_resumes_after_its_last_event_and_sees_only_its_tenants()
    {
        var store = new InMemoryLiveEvents();
        await store.AppendAsync(Draft(Acme, "incident-opened"), CancellationToken.None);
        await store.AppendAsync(Draft(Globex, "incident-opened"), CancellationToken.None);
        await store.AppendAsync(Draft(Acme, "decision-recorded"), CancellationToken.None);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var received = new List<LiveEvent>();
        IAsyncEnumerable<LiveEvent> stream = (await new StreamLiveEventsQueryHandler(store).Handle(new StreamLiveEventsQuery([Acme], 1), stop.Token)).Value;
        await foreach (LiveEvent item in stream)
        {
            received.Add(item);
            if (received.Count == 1)
            {
                break;
            }
        }

        LiveEvent only = received.ShouldHaveSingleItem();
        only.Kind.ShouldBe("decision-recorded");
        only.Sequence.ShouldBe(3);
    }

    [Fact]
    public void Routes_pick_channels_by_event_and_severity_and_ignore_unknown_channels()
    {
        var options = new NotificationOptions { WebBaseAddress = new Uri("http://web.example.test") };
        options.Channels.Add(new NotificationChannelOptions { Name = "oncall", Kind = "webhook", Url = new Uri("https://hooks.example.test/sentinel") });
        options.Routes.Add(new NotificationRouteOptions { Event = "incident-opened", MinimumSeverity = Severity.High, Channel = "oncall" });
        options.Routes.Add(new NotificationRouteOptions { Event = "plan-proposed", Channel = "oncall" });
        options.Routes.Add(new NotificationRouteOptions { Event = "plan-proposed", Channel = "not-configured" });

        NotificationRouter.Route(Draft(Acme, "incident-opened", Severity.Critical), options, Guid.NewGuid).ShouldHaveSingleItem().Link!.ToString().ShouldStartWith("http://web.example.test/incidents/");
        NotificationRouter.Route(Draft(Acme, "incident-opened", Severity.Low), options, Guid.NewGuid).ShouldBeEmpty();
        NotificationRouter.Route(Draft(Acme, "plan-proposed"), options, Guid.NewGuid).ShouldHaveSingleItem().Channel.ShouldBe("oncall");
        NotificationRouter.Route(Draft(Acme, "evidence-updated"), options, Guid.NewGuid).ShouldBeEmpty();
    }

    [Fact]
    public void An_escalation_is_its_own_event_so_it_can_be_routed()
    {
        LiveEventDraft draft = LiveEventMapper.From(new IncidentStatusChanged
        {
            TenantId = Acme,
            EnvironmentId = EnvironmentId.From("reference"),
            IncidentId = IncidentId.New(),
            Status = IncidentStatus.Escalated,
            Reason = "Abstained.",
            ChangedAt = DateTimeOffset.UnixEpoch,
            StreamVersion = 9
        });

        draft.Kind.ShouldBe(LiveEventKinds.IncidentEscalated);
        draft.Summary.ShouldContain("Abstained.");
    }

    private static LiveEventDraft Draft(TenantId tenant, string kind, Severity? severity = null) =>
        new(tenant, EnvironmentId.From("reference"), IncidentId.New(), kind, 1, kind, severity, DateTimeOffset.UnixEpoch);

    private sealed class InMemoryLiveEvents : ILiveEventStore
    {
        private readonly List<LiveEvent> _events = [];

        public Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken)
        {
            _events.Add(new LiveEvent(_events.Count + 1, draft.TenantId, draft.EnvironmentId, draft.IncidentId, draft.Kind, draft.StreamVersion, draft.Summary, draft.Severity, draft.OccurredAt));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LiveEvent>>([.. _events.Where(live => live.Sequence > after && tenants.Contains(live.TenantId)).Take(limit)]);

        public Task<long> LatestAsync(CancellationToken cancellationToken) => Task.FromResult((long)_events.Count);
    }
}
