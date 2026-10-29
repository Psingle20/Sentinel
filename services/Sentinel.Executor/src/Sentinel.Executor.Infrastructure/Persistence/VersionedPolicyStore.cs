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

    public async Task<bool> AppendAsync(PolicyDocument document, string author, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "insert into executor.policy_versions (version, document, author, published_at) values (@version, @document, @author, @published_at)");
        command.Parameters.AddWithValue("version", document.Version);
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(document, FilePolicyStore.Json) });
        command.Parameters.AddWithValue("author", author);
        command.Parameters.AddWithValue("published_at", publishedAt);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }
    }
}
