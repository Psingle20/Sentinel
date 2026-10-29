using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Sentinel.Gateway.Application.Approvals;
using Sentinel.Gateway.Infrastructure;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Sentinel.Gateway.Tests;

/// <summary>Api's local signer against a real PostgreSQL: what it signs verifies with what it publishes.</summary>
public sealed class LocalApprovalSignerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await new ApiSchema(_dataSource).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task An_attestation_verifies_against_the_published_key_and_tampering_breaks_it()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
        using LocalApprovalSigner signer = Signer(time);

        SignedAttestation signed = await signer.SignAsync(Claims(time.GetUtcNow()), CancellationToken.None);
        ApprovalKeySet keys = await signer.PublishedKeysAsync(CancellationToken.None);

        Verify(signed.Token, keys).ShouldBeTrue();
        string[] parts = signed.Token.Split('.');
        string tampered = $"{parts[0]}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{\"planHash\":\"sha256:other\"}"))}.{parts[2]}";
        Verify(tampered, keys).ShouldBeFalse();
        keys.Keys.ShouldAllBe(key => key.KeyType == "EC" && key.Curve == "P-256" && key.Algorithm == "ES256");
    }

    [Fact]
    public async Task After_rotation_the_previous_key_stays_published_until_its_attestations_expire()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
        using LocalApprovalSigner signer = Signer(time);
        SignedAttestation before = await signer.SignAsync(Claims(time.GetUtcNow()), CancellationToken.None);

        time.Advance(TimeSpan.FromHours(13));
        SignedAttestation after = await signer.SignAsync(Claims(time.GetUtcNow()), CancellationToken.None);
        ApprovalKeySet keys = await signer.PublishedKeysAsync(CancellationToken.None);

        after.KeyId.ShouldNotBe(before.KeyId);
        Verify(before.Token, keys).ShouldBeTrue();
        Verify(after.Token, keys).ShouldBeTrue();

        time.Advance(TimeSpan.FromHours(1));
        (await signer.PublishedKeysAsync(CancellationToken.None)).Keys.Select(key => key.KeyId).ShouldBe([after.KeyId]);
    }

    private LocalApprovalSigner Signer(TimeProvider time) => new(
        _dataSource,
        time,
        Microsoft.Extensions.Options.Options.Create(new SigningOptions { RotateAfter = TimeSpan.FromHours(12) }),
        Microsoft.Extensions.Options.Options.Create(new ApprovalOptions { AttestationLifetime = TimeSpan.FromMinutes(15) }),
        NullLogger<LocalApprovalSigner>.Instance);

    private static ApprovalAttestationClaims Claims(DateTimeOffset now) => new()
    {
        AttestationId = AttestationId.New(),
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        IncidentId = IncidentId.New(),
        PlanId = PlanId.New(),
        PlanVersion = 1,
        PlanHash = "sha256:plan",
        ApproverId = "responder@example.test",
        ApprovalScope = ApprovalAttestation.ExecutePlanScope,
        PolicyVersion = "static-2026.10.1",
        IssuedAt = now,
        ExpiresAt = now.AddMinutes(15),
        Issuer = ApprovalAttestation.Issuer,
        Audience = ApprovalAttestation.Audience
    };

    // Verification exactly as a consumer of the published key set would do it.
    private static bool Verify(string token, ApprovalKeySet keys)
    {
        string[] parts = token.Split('.');
        ApprovalAttestationHeader header = JsonSerializer.Deserialize<ApprovalAttestationHeader>(Base64Url.DecodeFromChars(parts[0]), SentinelJson.Options)!;
        ApprovalVerificationKey? key = keys.Keys.FirstOrDefault(candidate => candidate.KeyId == header.KeyId);
        if (key is null)
        {
            return false;
        }

        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64Url.DecodeFromChars(key.X), Y = Base64Url.DecodeFromChars(key.Y) }
        });
        return ecdsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256);
    }
}
