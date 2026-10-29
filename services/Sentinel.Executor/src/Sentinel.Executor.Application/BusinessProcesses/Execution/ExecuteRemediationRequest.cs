using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

/// <summary>
/// Runtime asks Executor to execute an approved plan (ADR-006). The request carries Runtime's message
/// itself, because <see cref="AttestationVerifier"/> binds the attestation to exactly that message.
/// </summary>
public sealed record ExecuteRemediationRequest(ExecuteRemediation Message) : IRequest;
