using System.Security.Cryptography;
using System.Text.Json;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Capture;

/// <summary>A query a replay needed that the execution never captured. A replay never falls back to a live provider.</summary>
public sealed class NotCapturedException(string kind, string requestHash)
    : HttpRequestException($"The {kind} query {requestHash[..12]} is not among the execution's captured inputs; a replay never calls a live provider.");
