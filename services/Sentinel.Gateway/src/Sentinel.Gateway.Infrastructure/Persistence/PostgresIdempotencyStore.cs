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

/// <summary>
/// Idempotency keys in Api's database. A claim inserts the key, or takes over one older than a day;
/// otherwise the stored row decides between a replay, a mismatch and a request still running.
/// </summary>
internal sealed class PostgresIdempotencyStore(NpgsqlDataSource dataSource) : IIdempotencyStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    public async Task<IdempotencyClaim> ClaimAsync(string actor, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand claim = dataSource.CreateCommand(
            """
            insert into api.idempotency_keys (actor, key, request_hash, created_at) values (@actor, @key, @hash, @now)
            on conflict (actor, key) do update
                set request_hash = excluded.request_hash, created_at = excluded.created_at, status_code = null, content_type = null, body = null, completed_at = null
                where api.idempotency_keys.created_at < @expired
            returning 1
            """))
        {
            claim.Parameters.AddWithValue("actor", actor);
            claim.Parameters.AddWithValue("key", key);
            claim.Parameters.AddWithValue("hash", requestHash);
            claim.Parameters.AddWithValue("now", now);
            claim.Parameters.AddWithValue("expired", now - Lifetime);
            if (await claim.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return new IdempotencyClaim.Claimed();
            }
        }

        await using NpgsqlCommand existing = dataSource.CreateCommand("select request_hash, status_code, content_type, body from api.idempotency_keys where actor = @actor and key = @key");
        existing.Parameters.AddWithValue("actor", actor);
        existing.Parameters.AddWithValue("key", key);
        await using NpgsqlDataReader reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Released between the two statements: the client may retry.
            return new IdempotencyClaim.InProgress();
        }

        if (reader.GetString(0) != requestHash)
        {
            return new IdempotencyClaim.Mismatch();
        }

        if (await reader.IsDBNullAsync(1, cancellationToken))
        {
            return new IdempotencyClaim.InProgress();
        }

        string? contentType = await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2);
        string body = await reader.IsDBNullAsync(3, cancellationToken) ? string.Empty : reader.GetString(3);
        return new IdempotencyClaim.Replay(new StoredResponse(reader.GetInt32(1), contentType, body));
    }

    public async Task CompleteAsync(string actor, string key, StoredResponse response, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "update api.idempotency_keys set status_code = @status, content_type = @content_type, body = @body, completed_at = @now where actor = @actor and key = @key");
        command.Parameters.AddWithValue("status", response.StatusCode);
        command.Parameters.AddWithValue("content_type", (object?)response.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("body", response.Body);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Only an unfinished reservation is released; a recorded response stays for replays.
    public async Task ReleaseAsync(string actor, string key, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("delete from api.idempotency_keys where actor = @actor and key = @key and completed_at is null");
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
