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
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessTasks.Commands.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Live;

namespace Sentinel.Gateway.Tests;

public sealed class ControlPlaneTests
{
    // The control plane's handlers behind the calls these tests make, composed as the host composes them.
    private sealed class ControlPlane(IExecutorControlPlane executor, IConfigurationOwners owners, IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        private readonly ControlPlaneAudit _audit = new(audit, unitOfWork, time);
        private readonly OperatorAttestations _attestations = new(new StubSigner(), Microsoft.Extensions.Options.Options.Create(new ApprovalOptions()), time);

        public async Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken) =>
            (await new PublishPolicyCommandHandler(executor, _attestations, _audit).Handle(new PublishPolicyCommand(document, expectedVersion, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) =>
            (await new SetEmergencyStopCommandHandler(executor, _attestations, _audit).Handle(new SetEmergencyStopCommand(engaged, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> PublishConfigurationAsync(string area, JsonElement document, string expectedVersion, string actor, CancellationToken cancellationToken) =>
            (await new PublishConfigurationCommandHandler(owners, _audit).Handle(new PublishConfigurationCommand(area, document, expectedVersion, actor), cancellationToken)).Value;

        public async Task<ControlPlaneResponse> ConfigurationAsync(string area, CancellationToken cancellationToken) =>
            (await new GetConfigurationQueryHandler(owners).Handle(new GetConfigurationQuery(area), cancellationToken)).Value;
    }
    [Fact]
    public async Task Every_control_plane_change_is_audited_whether_it_succeeded_or_was_refused()
    {
        var audit = new RecordingAudit();
        var service = new ControlPlane(new RefusingExecutor(), new RecordingOwners(), audit, new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "policy-2026.10.3", "rules": [] }""");

        ControlPlaneResponse published = await service.PublishPolicyAsync(document.RootElement, "policy-2026.10.2", "operator@example.test", CancellationToken.None);
        await service.SetEmergencyStopAsync(true, "operator@example.test", CancellationToken.None);

        published.IsSuccess.ShouldBeFalse();
        audit.Entries.Select(entry => (entry.Action, entry.Target)).ShouldBe([("policy.publish", "policy/policy-2026.10.3"), ("controls.emergency-stop.engage", "executor/emergency-stop")]);
        audit.Entries.ShouldAllBe(entry => entry.Actor == "operator@example.test");
    }

    [Fact]
    public async Task A_configuration_change_goes_to_the_area_that_owns_it_and_is_audited()
    {
        var audit = new RecordingAudit();
        var owners = new RecordingOwners();
        var service = new ControlPlane(new RefusingExecutor(), owners, audit, new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "runtime-2026.10.29.1", "detection": [] }""");

        ControlPlaneResponse published = await service.PublishConfigurationAsync(ConfigurationAreas.Runtime, document.RootElement, "deployment", "operator@example.test", CancellationToken.None);

        published.StatusCode.ShouldBe(200);
        owners.Published.ShouldBe([(ConfigurationAreas.Runtime, "deployment", "operator@example.test")]);
        audit.Entries.ShouldBe([("operator@example.test", "configuration.publish", "runtime/runtime-2026.10.29.1")]);
    }

    [Fact]
    public async Task An_unknown_configuration_area_reaches_no_service()
    {
        var owners = new RecordingOwners();
        var service = new ControlPlane(new RefusingExecutor(), owners, new RecordingAudit(), new NoUnitOfWork(), new FakeTimeProvider(DateTimeOffset.UnixEpoch));
        using var document = JsonDocument.Parse("""{ "version": "x" }""");

        (await service.ConfigurationAsync("secrets", CancellationToken.None)).StatusCode.ShouldBe(404);
        (await service.PublishConfigurationAsync("secrets", document.RootElement, "deployment", "operator@example.test", CancellationToken.None)).StatusCode.ShouldBe(404);
        owners.Published.ShouldBeEmpty();
    }

    private sealed class RecordingOwners : IConfigurationOwners
    {
        private static readonly ControlPlaneResponse Done = new(200, null, null);

        public List<(string Area, string ExpectedVersion, string Author)> Published { get; } = [];

        public Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
        {
            Published.Add((area, expectedVersion, author));
            return Task.FromResult(Done);
        }
    }

    private sealed class RefusingExecutor : IExecutorControlPlane
    {
        private static readonly ControlPlaneResponse Refused = new(400, null, "Policy.Invalid: rule covers no environment.");
        private static readonly ControlPlaneResponse Done = new(204, null, null);

        public Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, string attestation, CancellationToken cancellationToken) => Task.FromResult(Refused);

        public Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, string attestation, CancellationToken cancellationToken) => Task.FromResult(Done);

        public Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, string attestation, CancellationToken cancellationToken) => Task.FromResult(Done);
    }

    private sealed class StubSigner : IApprovalSigner
    {
        public Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken) => Task.FromResult(new SignedAttestation("decision-token", "test-key"));

        public Task<SignedAttestation> SignOperatorAsync(OperatorAttestationClaims claims, CancellationToken cancellationToken) => Task.FromResult(new SignedAttestation($"operator:{claims.ActorId}:{claims.Scope}:{claims.Resource}", "test-key"));

        public Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken) => Task.FromResult(new ApprovalKeySet([]));
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<(string Actor, string Action, string Target)> Entries { get; } = [];

        public Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken)
        {
            Entries.Add((actor, action, target));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
