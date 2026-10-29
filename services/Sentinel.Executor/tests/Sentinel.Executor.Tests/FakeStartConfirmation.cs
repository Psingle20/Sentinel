using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

internal sealed class FakeStartConfirmation : IStartConfirmation
{
    public bool Confirm { get; set; } = true;

    public bool Unreachable { get; set; }

    public int Calls { get; private set; }

    public Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Unreachable)
        {
            throw new HttpRequestException("Runtime is down.");
        }

        return Task.FromResult(new RemediationStartResponse(Confirm, Confirm ? null : "The plan was superseded."));
    }
}
