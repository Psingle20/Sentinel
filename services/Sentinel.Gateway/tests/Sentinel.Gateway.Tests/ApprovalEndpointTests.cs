using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Sentinel.Gateway.Application.Incidents;
using Sentinel.Gateway.Domain.Approvals;

namespace Sentinel.Gateway.Tests;

/// <summary>
/// The approval endpoints over HTTP, through the real host and database: routes, authorization,
/// tenant scoping, status codes, headers and bodies, and what is relayed to Runtime. Written before
/// the endpoints moved out of <c>Program.cs</c> (ADR-020), so the move is shown to change none of it.
/// </summary>
public sealed class ApprovalEndpointTests(GatewayFixture gateway) : IClassFixture<GatewayFixture>
{
    private const string Acme = "acme";
    private const string Approver = "responder@example.test";
    private const string Viewer = "viewer@example.test";

    private readonly GatewayHost _host = gateway.Host;
    private readonly HttpClient _client = gateway.Client;

    [Fact]
    public async Task Approving_the_current_plan_answers_202_with_the_receipt_at_the_incident_and_relays_the_attestation()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        RemediationPlan plan = incident.Plan!.Plan;

        using HttpResponseMessage response = await PostAsync(Path(incident, "approve"), new { planVersion = plan.PlanVersion, planHash = plan.PlanHash });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.OriginalString.ShouldBe($"/api/v1/incidents/{incident.Summary.IncidentId}");
        response.Content.Headers.ContentType!.ToString().ShouldBe("application/json; charset=utf-8");
        using JsonDocument receipt = await JsonAsync(response);
        Names(receipt).ShouldBe(["approvalId", "decision", "attestationId", "decidedAt", "expiresAt"]);
        receipt.RootElement.GetProperty("decision").GetString().ShouldBe("approved");
        receipt.RootElement.GetProperty("decidedAt").GetDateTimeOffset().ShouldBe(GatewayHost.Now);
        receipt.RootElement.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(GatewayHost.Now.AddMinutes(15));

        (string producer, JsonElement relayed) = (await RelayedAsync(RecordApproval.MessageType, incident)).ShouldHaveSingleItem();
        producer.ShouldBe("sentinel-api");
        relayed.GetProperty("planHash").GetString().ShouldBe(plan.PlanHash);
        relayed.GetProperty("approverId").GetString().ShouldBe(Approver);
        relayed.GetProperty("attestation").GetString()!.Split('.').Length.ShouldBe(3);
    }

    [Fact]
    public async Task Approving_again_with_a_new_key_returns_the_same_receipt_and_relays_nothing_more()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        RemediationPlan plan = incident.Plan!.Plan;
        var body = new { planVersion = plan.PlanVersion, planHash = plan.PlanHash };

        using HttpResponseMessage first = await PostAsync(Path(incident, "approve"), body);
        using HttpResponseMessage second = await PostAsync(Path(incident, "approve"), body);

        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        (await RelayedAsync(RecordApproval.MessageType, incident)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_changed_hash_another_version_or_a_plan_no_longer_awaiting_approval_is_a_409_problem_with_its_code()
    {
        IncidentDetail current = _host.Runtime.Add(Acme);
        IncidentDetail executing = _host.Runtime.Add(Acme, PlanStatus.Executing);
        RemediationPlan plan = current.Plan!.Plan;

        await ShouldBeProblemAsync(await PostAsync(Path(current, "approve"), new { planVersion = plan.PlanVersion, planHash = "sha256:stale" }), HttpStatusCode.Conflict, "Approval.HashMismatch");
        await ShouldBeProblemAsync(await PostAsync(Path(current, "approve"), new { planVersion = plan.PlanVersion + 1, planHash = plan.PlanHash }), HttpStatusCode.Conflict, "Approval.NotCurrentPlan");
        await ShouldBeProblemAsync(
            await PostAsync(Path(executing, "approve"), new { planVersion = executing.Plan!.Plan.PlanVersion, planHash = executing.Plan.Plan.PlanHash }),
            HttpStatusCode.Conflict,
            "Approval.NotAwaitingApproval",
            "The plan is Executing, not awaiting approval.");
        (await RelayedAsync(RecordApproval.MessageType, current)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_and_answers_202_with_a_receipt_that_carries_no_attestation()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        int version = incident.Plan!.Plan.PlanVersion;

        await ShouldBeProblemAsync(await PostAsync(Path(incident, "reject"), new { planVersion = version, reason = "  " }), HttpStatusCode.UnprocessableEntity, "Approval.ReasonRequired");
        using HttpResponseMessage response = await PostAsync(Path(incident, "reject"), new { planVersion = version, reason = "Outside the change window." });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location!.OriginalString.ShouldBe($"/api/v1/incidents/{incident.Summary.IncidentId}");
        using JsonDocument receipt = await JsonAsync(response);
        Names(receipt).ShouldBe(["approvalId", "decision", "decidedAt"]);
        receipt.RootElement.GetProperty("decision").GetString().ShouldBe("rejected");
        (await RelayedAsync(RecordRejection.MessageType, incident)).ShouldHaveSingleItem().Payload.GetProperty("reason").GetString().ShouldBe("Outside the change window.");
        await ShouldBeProblemAsync(
            await PostAsync(Path(incident, "approve"), new { planVersion = version, planHash = incident.Plan.Plan.PlanHash }),
            HttpStatusCode.Conflict,
            "Approval.AlreadyDecided");
    }

    [Fact]
    public async Task A_revocation_withdraws_an_approval_until_execution_starts()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        IncidentDetail started = _host.Runtime.Add(Acme);
        RemediationPlan plan = incident.Plan!.Plan;

        await ShouldBeProblemAsync(await PostAsync(Path(incident, "revoke"), new { planVersion = plan.PlanVersion, reason = "Wrong target." }), HttpStatusCode.Conflict, "Approval.NothingToRevoke");
        using HttpResponseMessage approved = await PostAsync(Path(incident, "approve"), new { planVersion = plan.PlanVersion, planHash = plan.PlanHash });
        using JsonDocument approval = await JsonAsync(approved);
        using HttpResponseMessage revoked = await PostAsync(Path(incident, "revoke"), new { planVersion = plan.PlanVersion, reason = "Wrong target." });

        revoked.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using JsonDocument receipt = await JsonAsync(revoked);
        receipt.RootElement.GetProperty("decision").GetString().ShouldBe("revoked");
        receipt.RootElement.GetProperty("approvalId").GetGuid().ShouldBe(approval.RootElement.GetProperty("approvalId").GetGuid());
        RecordRevocation accepted = _host.Runtime.Revocations.ShouldHaveSingleItem();
        accepted.IncidentId.ShouldBe(incident.Summary.IncidentId);
        accepted.Attestation.ShouldNotBeNullOrEmpty();
        (await RelayedAsync(RecordRevocation.MessageType, incident)).ShouldBeEmpty();

        using HttpResponseMessage startedApproval = await PostAsync(Path(started, "approve"), new { planVersion = started.Plan!.Plan.PlanVersion, planHash = started.Plan.Plan.PlanHash });
        startedApproval.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        _host.Runtime.StartExecution(started.Summary.IncidentId);
        await ShouldBeProblemAsync(await PostAsync(Path(started, "revoke"), new { planVersion = started.Plan.Plan.PlanVersion, reason = "Too late." }), HttpStatusCode.Conflict, "Approval.ExecutionStarted");
    }

    [Fact]
    public async Task Another_tenants_incident_or_an_unknown_one_is_a_plain_not_found_and_nothing_is_decided()
    {
        IncidentDetail foreign = _host.Runtime.Add("globex");
        RemediationPlan plan = foreign.Plan!.Plan;

        using HttpResponseMessage response = await PostAsync(Path(foreign, "approve"), new { planVersion = plan.PlanVersion, planHash = plan.PlanHash });
        using HttpResponseMessage unknown = await PostAsync($"/api/v1/incidents/{Guid.NewGuid()}/plans/{Guid.NewGuid()}/approve", new { planVersion = 1, planHash = plan.PlanHash });

        await ShouldBePlainNotFoundAsync(response);
        await ShouldBePlainNotFoundAsync(unknown);
        (await RelayedAsync(RecordApproval.MessageType, foreign)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deciding_needs_the_approver_role_and_an_idempotency_key()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        RemediationPlan plan = incident.Plan!.Plan;
        var body = new { planVersion = plan.PlanVersion, planHash = plan.PlanHash };

        using HttpResponseMessage viewer = await PostAsync(Path(incident, "approve"), body, Viewer);
        using HttpResponseMessage keyless = await PostAsync(Path(incident, "approve"), body, key: null);

        viewer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await ShouldBeProblemAsync(keyless, HttpStatusCode.BadRequest, "Idempotency.KeyRequired");
        (await RelayedAsync(RecordApproval.MessageType, incident)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retry_with_the_same_key_replays_the_first_answer()
    {
        IncidentDetail incident = _host.Runtime.Add(Acme);
        RemediationPlan plan = incident.Plan!.Plan;
        var body = new { planVersion = plan.PlanVersion, planHash = plan.PlanHash };
        string key = $"approve-{plan.PlanId}-v{plan.PlanVersion}";

        using HttpResponseMessage first = await PostAsync(Path(incident, "approve"), body, key: key);
        using HttpResponseMessage retry = await PostAsync(Path(incident, "approve"), body, key: key);

        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retry.Headers.GetValues("Idempotent-Replayed").ShouldHaveSingleItem().ShouldBe("true");
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_verification_keys_are_published_without_authentication()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApprovalAttestation.KeysPath, UriKind.Relative));
        request.Headers.Add("X-Sentinel-User", "nobody@example.test");

        using HttpResponseMessage response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument keys = await JsonAsync(response);
        JsonElement key = keys.RootElement.GetProperty("keys").EnumerateArray().ShouldHaveSingleItem();
        key.TryGetProperty("d", out _).ShouldBeFalse();
    }

    private static string Path(IncidentDetail incident, string decision) =>
        $"/api/v1/incidents/{incident.Summary.IncidentId}/plans/{incident.Plan!.Plan.PlanId}/{decision}";

    private async Task<HttpResponseMessage> PostAsync(string path, object body, string user = Approver, string? key = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Sentinel-User", user);
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key.Length == 0 ? $"test-{Guid.NewGuid():N}" : key);
        }

        return await _client.SendAsync(request);
    }

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static string[] Names(JsonDocument document) => [.. document.RootElement.EnumerateObject().Select(property => property.Name)];

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? detail = null)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(status);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            using JsonDocument problem = await JsonAsync(response);
            problem.RootElement.GetProperty("status").GetInt32().ShouldBe((int)status);
            problem.RootElement.GetProperty("title").GetString().ShouldBe(code);
            problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
            problem.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
            if (detail is not null)
            {
                problem.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
            }
        }
    }

    // A bare 404, which the status-code pages turn into a problem document without an error code, so the incident's existence is not disclosed.
    private static async Task ShouldBePlainNotFoundAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using JsonDocument problem = await JsonAsync(response);
        problem.RootElement.GetProperty("title").GetString().ShouldBe("Not Found");
        problem.RootElement.TryGetProperty("code", out _).ShouldBeFalse();
    }

    // What the Gateway enqueued for Runtime about the incident: the producer and the payload of each message.
    private async Task<List<(string Producer, JsonElement Payload)>> RelayedAsync(string messageType, IncidentDetail incident)
    {
        await using var dataSource = NpgsqlDataSource.Create(gateway.ConnectionString);
        await using NpgsqlCommand command = dataSource.CreateCommand("select producer, payload from messaging.outbox where message_type = @type and correlation_id = @incident");
        command.Parameters.AddWithValue("type", messageType);
        command.Parameters.AddWithValue("incident", incident.Summary.IncidentId.ToString());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        var relayed = new List<(string, JsonElement)>();
        while (await reader.ReadAsync())
        {
            using var payload = JsonDocument.Parse(reader.GetString(1));
            relayed.Add((reader.GetString(0), payload.RootElement.Clone()));
        }

        return relayed;
    }
}
