using System.Security.Cryptography;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Application.BusinessTasks.Commands.Execution;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Shouldly;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Options;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Tests;

public sealed class AttestationTests : IDisposable
{
    private readonly TestSigner _signer = new();
    private readonly FakeTimeProvider _time = new(Commands.Now);

    public void Dispose() => _signer.Dispose();

    private Task<CleanArchitecture.BuildingBlocks.Result<ApprovalAttestationClaims>> Verify(ExecuteRemediation command) =>
        new AttestationVerifier(_signer, _time).VerifyAsync(command.Attestation, command, CancellationToken.None);

    [Fact]
    public async Task A_valid_attestation_is_accepted()
    {
        (await Verify(Commands.Execute(_signer))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ExecuteRemediation command = Commands.Execute(_signer);
        ApprovalAttestationClaims claims = Commands.Claims(command.IncidentId, command.Plan);

        (await Verify(command with { Attestation = _signer.Sign(claims, key: attacker) })).Error.Description.ShouldContain("signature");
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("hash")]
    [InlineData("incident")]
    [InlineData("scope")]
    public async Task Claims_that_do_not_bind_to_this_execution_are_refused(string problem)
    {
        ExecuteRemediation command = Commands.Execute(_signer, claims: claims => problem switch
        {
            "audience" => claims with { Audience = "sentinel-runtime" },
            "expired" => claims with { ExpiresAt = Commands.Now.AddMinutes(-5) },
            "hash" => claims with { PlanHash = "sha256:other" },
            "incident" => claims with { IncidentId = IncidentId.New() },
            _ => claims with { ApprovalScope = "read" }
        });

        (await Verify(command)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_key_and_a_malformed_token_are_refused()
    {
        ExecuteRemediation command = Commands.Execute(_signer);
        ApprovalAttestationClaims claims = Commands.Claims(command.IncidentId, command.Plan);

        (await Verify(command with { Attestation = _signer.Sign(claims, keyId: "rotated-away") })).Error.Description.ShouldContain("not a published");
        (await Verify(command with { Attestation = "not-a-jws" })).IsFailure.ShouldBeTrue();
        (await Verify(command with { Attestation = string.Empty })).IsFailure.ShouldBeTrue();
    }
}
