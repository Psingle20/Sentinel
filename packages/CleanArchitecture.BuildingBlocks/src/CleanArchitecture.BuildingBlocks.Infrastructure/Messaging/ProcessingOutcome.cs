using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

public enum ProcessingOutcome
{
    /// <summary>Handled and committed. Acknowledge the delivery.</summary>
    Processed,

    /// <summary>Already handled by this consumer. Acknowledge without handling again.</summary>
    Duplicate,

    /// <summary>Handling failed and was rolled back. Leave the delivery for a retry.</summary>
    Failed,

    /// <summary>Unsupported type or version, or a malformed payload. Dead-letter it; retrying cannot help.</summary>
    Rejected
}
