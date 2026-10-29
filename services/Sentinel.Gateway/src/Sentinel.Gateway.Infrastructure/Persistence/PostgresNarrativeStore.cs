using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Analytics;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Postmortems;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Infrastructure.Persistence;

/// <summary>Postmortem narratives with optimistic versioning: an edit saves only over the version it started from.</summary>
internal sealed class PostgresNarrativeStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : INarrativeStore
{
    public async Task<PostmortemNarrative?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select markdown, version, edited_by, edited_at from api.postmortem_narratives where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PostmortemNarrative(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken))
            : null;
    }

    public async Task<bool> SaveAsync(IncidentId incidentId, PostmortemNarrative narrative, int expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(narrative);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            expectedVersion == 0
                ? "insert into api.postmortem_narratives (incident_id, markdown, version, edited_by, edited_at) values (@incident_id, @markdown, @version, @edited_by, @edited_at) on conflict do nothing"
                : "update api.postmortem_narratives set markdown = @markdown, version = @version, edited_by = @edited_by, edited_at = @edited_at where incident_id = @incident_id and version = @expected",
            current.Connection,
            current);
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("markdown", narrative.Markdown);
        command.Parameters.AddWithValue("version", narrative.Version);
        command.Parameters.AddWithValue("edited_by", narrative.EditedBy);
        command.Parameters.AddWithValue("edited_at", narrative.EditedAt);
        if (expectedVersion != 0)
        {
            command.Parameters.AddWithValue("expected", expectedVersion);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
