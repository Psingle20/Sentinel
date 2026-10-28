using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

public sealed record OrderPlaced(Guid OrderId, decimal Amount);

/// <summary>Controls how many deliveries fail before handling succeeds.</summary>
public sealed class BillingBehavior
{
    private int _attempts;

    public int FailFirst { get; init; }

    public int Attempts => _attempts;

    internal bool NextAttemptFails() => Interlocked.Increment(ref _attempts) <= FailFirst;
}

/// <summary>The consuming service: writes an invoice in the same transaction as its inbox record.</summary>
internal sealed class CreateInvoiceOnOrderPlaced(IPostgresTransaction transaction, BillingBehavior behavior)
    : IIntegrationMessageHandler<OrderPlaced>
{
    internal const string CreateTable = "create table invoices (order_id uuid primary key, amount numeric not null)";

    public async Task<Result> HandleAsync(OrderPlaced message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (behavior.NextAttemptFails())
        {
            return Result.Failure(Error.Problem("Billing.Unavailable", "The billing ledger is unavailable."));
        }

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("insert into invoices (order_id, amount) values (@order_id, @amount)", current.Connection, current);
        command.Parameters.AddWithValue("order_id", message.OrderId);
        command.Parameters.AddWithValue("amount", message.Amount);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return Result.Success();
    }
}
