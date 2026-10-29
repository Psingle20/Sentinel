using System.Net.Http.Headers;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Infrastructure.Persistence;

/// <summary>
/// The current policy: the newest version published through the control plane (Phase 08), or the
/// repository's policy file when none has been. Published versions are kept with their author and
/// never edited, so every evaluation's version can be looked up later.
/// </summary>
internal sealed class VersionedPolicyStore(FilePolicyStore seed, NpgsqlDataSource dataSource) : IPolicyStore
{
    private const string UniqueViolation = "23505";

    public async Task<PolicyDocument> CurrentAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select document from executor.policy_versions order by published_at desc limit 1");
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<PolicyDocument>(json, FilePolicyStore.Json)!
            : seed.Current;
    }

    public async Task<IReadOnlyList<PolicyVersionInfo>> HistoryAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select version, author, published_at from executor.policy_versions order by published_at desc");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<PolicyVersionInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PolicyVersionInfo(reader.GetString(0), reader.GetString(1), await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken), "control-plane"));
        }

        history.Add(new PolicyVersionInfo(seed.Current.Version, "repository", DateTimeOffset.MinValue, "file"));
        return history;
    }

    /// <summary>
    /// Compare and append under a transaction-scoped advisory lock: of two operators publishing over the same version,
    /// exactly one succeeds and the other is told to reload, so neither change silently replaces the other.
    /// </summary>
    public async Task<bool> AppendAsync(PolicyDocument document, string basedOn, string author, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn);

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var lockCommand = new NpgsqlCommand("select pg_advisory_xact_lock(hashtext('executor.policy_versions'))", connection, transaction))
        {
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var headCommand = new NpgsqlCommand("select version from executor.policy_versions order by published_at desc limit 1", connection, transaction))
        {
            string head = await headCommand.ExecuteScalarAsync(cancellationToken) as string ?? seed.Current.Version;
            if (!string.Equals(head, basedOn, StringComparison.Ordinal))
            {
                return false;
            }
        }

        await using var insert = new NpgsqlCommand(
            "insert into executor.policy_versions (version, document, author, published_at) values (@version, @document, @author, @published_at)",
            connection,
            transaction);
        insert.Parameters.AddWithValue("version", document.Version);
        insert.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(document, FilePolicyStore.Json) });
        insert.Parameters.AddWithValue("author", author);
        insert.Parameters.AddWithValue("published_at", publishedAt);
        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }
    }
}
