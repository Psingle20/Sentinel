using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArchitecture.BuildingBlocks.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

/// <summary>
/// <see cref="IVersionedDocumentStore{TDocument}"/> in one PostgreSQL table per document. Publishes
/// of the same document are serialized by a transaction-scoped advisory lock, so the check that the
/// change is based on the newest version and the insert happen atomically.
/// </summary>
public sealed partial class PostgresVersionedDocumentStore<TDocument> : IVersionedDocumentStore<TDocument>
    where TDocument : class
{
    private const string UniqueViolation = "23505";

    // The owning service creates the table with its schema, which can happen after the first read.
    private const string UndefinedTable = "42P01";

    private readonly NpgsqlDataSource _dataSource;
    private readonly JsonSerializerOptions _json;
    private readonly string _table;

    /// <param name="dataSource">The owning service's database.</param>
    /// <param name="table">A schema-qualified table name such as <c>runtime.configuration_versions</c>.</param>
    /// <param name="json">How the document is serialized.</param>
    public PostgresVersionedDocumentStore(NpgsqlDataSource dataSource, string table, JsonSerializerOptions json)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(json);

        _dataSource = dataSource;
        _json = json;
        _table = ValidTable(table);
    }

    /// <summary>The idempotent DDL for <paramref name="table"/>; run it with the owning service's schema.</summary>
    public static string CreateTableSql(string table)
    {
        string name = ValidTable(table);
        return $"""
            create table if not exists {name} (
                sequence bigint generated always as identity primary key,
                version text not null unique,
                based_on text,
                document jsonb not null,
                author text not null,
                published_at timestamptz not null
            );
            """;
    }

    public async Task<PublishedDocument<TDocument>?> CurrentAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand($"select document, version, author, published_at, based_on from {_table} order by sequence desc limit 1");
        await using NpgsqlDataReader? reader = await ReadOrNullAsync(command, cancellationToken);
        if (reader is null || !await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        TDocument document = JsonSerializer.Deserialize<TDocument>(reader.GetString(0), _json)
            ?? throw new InvalidOperationException($"The current document in {_table} is empty.");
        return new PublishedDocument<TDocument>(document, await ReadVersionAsync(reader, 1, cancellationToken));
    }

    public async Task<IReadOnlyList<DocumentVersion>> HistoryAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand($"select version, author, published_at, based_on from {_table} order by sequence desc limit @limit");
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
        await using NpgsqlDataReader? reader = await ReadOrNullAsync(command, cancellationToken);
        var history = new List<DocumentVersion>();
        while (reader is not null && await reader.ReadAsync(cancellationToken))
        {
            history.Add(await ReadVersionAsync(reader, 0, cancellationToken));
        }

        return history;
    }

    public async Task<AppendOutcome> AppendAsync(TDocument document, DocumentVersion version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(version);

        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var lockCommand = new NpgsqlCommand("select pg_advisory_xact_lock(hashtext(@table))", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("table", _table);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var headCommand = new NpgsqlCommand($"select version from {_table} order by sequence desc limit 1", connection, transaction))
        {
            if (await headCommand.ExecuteScalarAsync(cancellationToken) is string head && !string.Equals(head, version.BasedOn, StringComparison.Ordinal))
            {
                return AppendOutcome.Conflict;
            }
        }

        await using var insert = new NpgsqlCommand(
            $"insert into {_table} (version, based_on, document, author, published_at) values (@version, @based_on, @document, @author, @published_at)",
            connection,
            transaction);
        insert.Parameters.AddWithValue("version", version.Version);
        insert.Parameters.AddWithValue("based_on", (object?)version.BasedOn ?? DBNull.Value);
        insert.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(document, _json) });
        insert.Parameters.AddWithValue("author", version.Author);
        insert.Parameters.AddWithValue("published_at", version.PublishedAt);
        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return AppendOutcome.DuplicateVersion;
        }

        await transaction.CommitAsync(cancellationToken);
        return AppendOutcome.Appended;
    }

    private static async Task<NpgsqlDataReader?> ReadOrNullAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            return null;
        }
    }

    private static async Task<DocumentVersion> ReadVersionAsync(NpgsqlDataReader reader, int first, CancellationToken cancellationToken) =>
        new(
            reader.GetString(first),
            reader.GetString(first + 1),
            await reader.GetFieldValueAsync<DateTimeOffset>(first + 2, cancellationToken),
            await reader.IsDBNullAsync(first + 3, cancellationToken) ? null : reader.GetString(first + 3));

    // The table name is interpolated into SQL, so only a plain schema-qualified identifier is accepted.
    private static string ValidTable(string table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        return TableName().IsMatch(table) ? table : throw new ArgumentException($"'{table}' is not a schema-qualified table name.", nameof(table));
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*\\.[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableName();
}
