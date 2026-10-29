using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;

namespace Sentinel.Runtime.Tests;

/// <summary>One connection and transaction, as the building blocks' unit of work gives a request scope.</summary>
internal sealed class SingleTransaction(NpgsqlDataSource dataSource) : IPostgresTransaction, IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    public async Task<NpgsqlTransaction> GetTransactionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            _connection = await dataSource.OpenConnectionAsync(cancellationToken);
            _transaction = await _connection.BeginTransactionAsync(cancellationToken);
        }

        return _transaction;
    }

    public async Task CommitAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
