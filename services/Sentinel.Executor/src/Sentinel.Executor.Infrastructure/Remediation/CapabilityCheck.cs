using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Application.Abstractions.Remediation;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>Defence in depth: an adapter acts only under a capability for exactly its action, resource and scope.</summary>
internal static class CapabilityCheck
{
    public static AdapterResult? Refuse(Capability capability, string action, string resource, TimeProvider time)
    {
        CleanArchitecture.BuildingBlocks.Result authorized = capability.Authorizes(action, resource, capability.Scope, time.GetUtcNow());
        return authorized.IsSuccess ? null : new AdapterResult(AdapterOutcome.Rejected, authorized.Error.Description);
    }
}
