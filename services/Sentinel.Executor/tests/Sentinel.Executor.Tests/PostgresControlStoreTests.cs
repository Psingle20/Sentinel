using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Sentinel.Executor.Tests;

/// <summary>Operator controls against a real PostgreSQL (review finding F-008): a change and its audit commit together.</summary>
public sealed class PostgresControlStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await new ExecutorSchema(_dataSource).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Every_committed_control_change_is_recorded_with_its_operator_newest_first()
    {
        await ChangeAsync(store => store.SetEmergencyStopAsync(true, "operator-a@example.test", CancellationToken.None), commit: true);
        await ChangeAsync(store => store.SetActionDisabledAsync("rollback", true, "operator-b@example.test", CancellationToken.None), commit: true);
        await ChangeAsync(store => store.SetEmergencyStopAsync(false, "operator-a@example.test", CancellationToken.None), commit: true);

        IReadOnlyList<ControlChange> history = await Store(new SingleTransaction(_dataSource)).HistoryAsync(10, CancellationToken.None);

        history.Select(change => (change.Control, change.Value, change.ChangedBy)).ShouldBe(
        [
            ("emergency-stop", false, "operator-a@example.test"),
            ("disabled:rollback", true, "operator-b@example.test"),
            ("emergency-stop", true, "operator-a@example.test")
        ]);
        (await Store(new SingleTransaction(_dataSource)).GetAsync(CancellationToken.None)).EmergencyStop.ShouldBeFalse();
    }

    [Fact]
    public async Task A_change_that_does_not_commit_leaves_neither_the_control_nor_an_audit_record()
    {
        await ChangeAsync(store => store.SetEmergencyStopAsync(true, "operator-c@example.test", CancellationToken.None), commit: false);

        (await Store(new SingleTransaction(_dataSource)).HistoryAsync(10, CancellationToken.None)).ShouldNotContain(change => change.ChangedBy == "operator-c@example.test");
        (await Store(new SingleTransaction(_dataSource)).GetAsync(CancellationToken.None)).EmergencyStop.ShouldBeFalse();
    }

    private PostgresControlStore Store(IPostgresTransaction transaction) => new(transaction, _dataSource);

    private async Task ChangeAsync(Func<PostgresControlStore, Task> change, bool commit)
    {
        await using var transaction = new SingleTransaction(_dataSource);
        await change(Store(transaction));
        if (commit)
        {
            await transaction.CommitAsync();
        }
    }

    /// <summary>One connection and transaction, as the building blocks' unit of work gives a request scope.</summary>
    private sealed class SingleTransaction(NpgsqlDataSource dataSource) : IPostgresTransaction, IAsyncDisposable
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
}
