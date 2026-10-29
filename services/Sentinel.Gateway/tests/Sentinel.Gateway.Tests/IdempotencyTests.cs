using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Gateway.Application.Idempotency;
using Sentinel.Gateway.Api;
using Shouldly;
using Sentinel.Gateway.Api.Idempotency;

namespace Sentinel.Gateway.Tests;

/// <summary>Phase 08 API principles: mutations require an idempotency key, and a retry never changes state twice.</summary>
public sealed class IdempotencyTests : IAsyncLifetime
{
    private readonly InMemoryIdempotencyStore _store = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _approvals;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IIdempotencyStore>(_store);
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestUser>("test", _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseIdempotencyKeys();
        _app.MapPost("/api/v1/plans/approve", () => Results.Accepted(value: new { approval = Interlocked.Increment(ref _approvals) })).RequireAuthorization();
        _app.MapPost("/api/v1/plans/fails", () => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable)).RequireAuthorization();
        _app.MapGet("/api/v1/plans", () => "read").RequireAuthorization();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private Task<HttpResponseMessage> PostAsync(string path, string? key, string body = """{"planVersion":1}""")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (key is not null)
        {
            request.Headers.Add(IdempotencyExtensions.Header, key);
        }

        return _client.SendAsync(request);
    }

    [Fact]
    public async Task A_mutation_without_a_key_is_refused_and_changes_nothing()
    {
        using HttpResponseMessage response = await PostAsync("/api/v1/plans/approve", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Idempotency.KeyRequired");
        _approvals.ShouldBe(0);
    }

    [Fact]
    public async Task A_retry_with_the_same_key_gets_the_first_response_without_a_second_change()
    {
        using HttpResponseMessage first = await PostAsync("/api/v1/plans/approve", "approve-plan-1");
        using HttpResponseMessage retry = await PostAsync("/api/v1/plans/approve", "approve-plan-1");

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await retry.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());
        retry.Headers.GetValues(IdempotencyExtensions.ReplayedHeader).ShouldBe(["true"]);
        _approvals.ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_refused()
    {
        using HttpResponseMessage first = await PostAsync("/api/v1/plans/approve", "approve-plan-2");
        using HttpResponseMessage reused = await PostAsync("/api/v1/plans/approve", "approve-plan-2", """{"planVersion":2}""");

        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        _approvals.ShouldBe(1);
    }

    [Fact]
    public async Task A_server_failure_is_not_kept_so_the_client_can_retry()
    {
        using HttpResponseMessage failed = await PostAsync("/api/v1/plans/fails", "retry-me");

        failed.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        _store.Keys.ShouldNotContain(("tester@example.test", "retry-me"));
    }

    [Fact]
    public async Task Reads_need_no_key()
    {
        using HttpResponseMessage response = await _client.GetAsync(new Uri("/api/v1/plans", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "tester@example.test")], "test")), "test")));
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<(string Actor, string Key), (string Hash, StoredResponse? Response)> _keys = new();

        public ICollection<(string Actor, string Key)> Keys => _keys.Keys;

        public Task<IdempotencyClaim> ClaimAsync(string actor, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (_keys.TryAdd((actor, key), (requestHash, null)))
            {
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Claimed());
            }

            (string hash, StoredResponse? response) = _keys[(actor, key)];
            if (hash != requestHash)
            {
                return Task.FromResult<IdempotencyClaim>(new IdempotencyClaim.Mismatch());
            }

            return Task.FromResult<IdempotencyClaim>(response is null ? new IdempotencyClaim.InProgress() : new IdempotencyClaim.Replay(response));
        }

        public Task CompleteAsync(string actor, string key, StoredResponse response, DateTimeOffset now, CancellationToken cancellationToken)
        {
            _keys[(actor, key)] = (_keys[(actor, key)].Hash, response);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string actor, string key, CancellationToken cancellationToken)
        {
            _keys.TryRemove((actor, key), out _);
            return Task.CompletedTask;
        }
    }
}
