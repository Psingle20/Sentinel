using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

/// <summary>
/// The scope's PostgreSQL transaction. Service repositories, including EF Core through
/// <c>Database.UseTransactionAsync</c>, join it so domain writes, the inbox record and outbox
/// messages commit together.
/// </summary>
public interface IPostgresTransaction
{
    /// <summary>Returns the scope's transaction, opening a connection and beginning it on first use.</summary>
    Task<NpgsqlTransaction> GetTransactionAsync(CancellationToken cancellationToken);
}
