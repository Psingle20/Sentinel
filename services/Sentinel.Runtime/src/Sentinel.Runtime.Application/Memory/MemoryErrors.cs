using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application.Memory;

public static class MemoryErrors
{
    public static readonly CleanArchitecture.BuildingBlocks.Error NotRemembered = CleanArchitecture.BuildingBlocks.Error.NotFound("Memory.NotRemembered", "The incident has no memory record.");

    public static readonly CleanArchitecture.BuildingBlocks.Error LegalHold = CleanArchitecture.BuildingBlocks.Error.Conflict("Memory.LegalHold", "The incident's memory is on legal hold and cannot be deleted.");
}
