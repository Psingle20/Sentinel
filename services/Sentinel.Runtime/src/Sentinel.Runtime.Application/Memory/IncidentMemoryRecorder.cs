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
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>Writes an incident's memory record whenever it is closed, in the same unit of work as the events.</summary>
public sealed class IncidentMemoryRecorder(IIncidentMemoryStore memory, IOptions<RuntimeOptions> options)
{
    public async Task RecordAsync(Incident incident, IncidentDetail detail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (incident.State is not (IncidentState.Resolved or IncidentState.Escalated))
        {
            return;
        }

        IncidentMemoryView? existing = await memory.GetAsync(incident.Id, cancellationToken);
        if (existing?.Retention.DeletedAt is not null)
        {
            // A privacy deletion is final: later events of the stream never bring the memory back.
            return;
        }

        MemoryOptions settings = options.Value.Memory;
        RetentionMetadata retention = existing?.Retention ?? new RetentionMetadata(now + settings.Retention, LegalHold: false, settings.PrivacyClass, settings.RedactionPolicyVersion);
        await memory.SaveAsync(IncidentMemoryBuilder.Build(incident, detail, retention, now), cancellationToken);
    }
}
