using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Infrastructure.Clients;
using Shouldly;

namespace Sentinel.Executor.Tests;

/// <summary>Executor's cache of the gateway's approval keys (review finding F-012): retirement, validity and staleness.</summary>
public sealed class ApiApprovalKeysTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));
    private readonly KeyEndpoint _gateway = new();
    private readonly ECDsa _current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsa _retiring = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _current.Dispose();
        _retiring.Dispose();
    }

    [Fact]
    public async Task A_key_the_gateway_stops_publishing_stops_verifying_once_the_set_is_read_again()
    {
        _gateway.Keys = [Published("current", _current), Published("retiring", _retiring)];
        using ApiApprovalKeys keys = Keys();
        (await keys.ResolveAsync("retiring", CancellationToken.None)).ShouldNotBeNull();

        _gateway.Keys = [Published("current", _current)];
        _time.Advance(ApiApprovalKeys.KeySetLifetime);

        (await keys.ResolveAsync("retiring", CancellationToken.None)).ShouldBeNull();
        (await keys.ResolveAsync("current", CancellationToken.None)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_key_is_trusted_only_inside_its_published_validity_window()
    {
        long now = _time.GetUtcNow().ToUnixTimeSeconds();
        _gateway.Keys = [Published("expired", _retiring, notBefore: now - 3600, expires: now - 1), Published("future", _current, notBefore: now + 3600)];
        using ApiApprovalKeys keys = Keys();

        (await keys.ResolveAsync("expired", CancellationToken.None)).ShouldBeNull();
        (await keys.ResolveAsync("future", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task A_set_that_cannot_be_read_for_too_long_trusts_no_key()
    {
        _gateway.Keys = [Published("current", _current)];
        using ApiApprovalKeys keys = Keys();
        (await keys.ResolveAsync("current", CancellationToken.None)).ShouldNotBeNull();

        _gateway.Down = true;
        _time.Advance(ApiApprovalKeys.KeySetLifetime);
        (await keys.ResolveAsync("current", CancellationToken.None)).ShouldNotBeNull("a short outage keeps the last set");

        _time.Advance(ApiApprovalKeys.MaxStaleness);
        (await keys.ResolveAsync("current", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_key_ids_do_not_flood_the_gateway()
    {
        _gateway.Keys = [Published("current", _current)];
        using ApiApprovalKeys keys = Keys();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            (await keys.ResolveAsync($"forged-{attempt}", CancellationToken.None)).ShouldBeNull();
        }

        _gateway.Reads.ShouldBe(1);
    }

    private ApiApprovalKeys Keys() => new(new SingleClientFactory(_gateway), _time, NullLogger<ApiApprovalKeys>.Instance);

    private ApprovalVerificationKey Published(string keyId, ECDsa key, long? notBefore = null, long? expires = null)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        return new ApprovalVerificationKey(
            keyId, "EC", "P-256", Base64Url.EncodeToString(parameters.Q.X), Base64Url.EncodeToString(parameters.Q.Y), ApprovalAttestation.Algorithm, "sig",
            notBefore ?? _time.GetUtcNow().AddDays(-1).ToUnixTimeSeconds(), expires);
    }

    private sealed class KeyEndpoint : HttpMessageHandler
    {
        public IReadOnlyList<ApprovalVerificationKey> Keys { get; set; } = [];

        public bool Down { get; set; }

        public int Reads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Reads++;
            if (Down)
            {
                throw new HttpRequestException("The gateway is unreachable.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ApprovalKeySet(Keys), options: SentinelJson.Options) });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://gateway.example.test") };
    }
}
