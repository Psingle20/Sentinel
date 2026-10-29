using Npgsql;
using NpgsqlTypes;
using Sentinel.AgentWorker.Application.Abstractions.Persistence;

namespace Sentinel.AgentWorker.Infrastructure.Persistence;

/// <summary>Model spend summed from the estimated cost each execution recorded; records made before costs were kept count as zero.</summary>
internal sealed class PostgresModelSpend(NpgsqlDataSource dataSource) : IModelSpend
{
    public async Task<double> SpentSinceAsync(DateTimeOffset since, string? model, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            select coalesce(sum((record ->> 'estimatedCost')::double precision), 0)
            from agentworker.executions
            where started_at >= @since
              and (@model::text is null or record ->> 'model' = @model)
            """);
        command.Parameters.AddWithValue("since", since == DateTimeOffset.MinValue ? DateTimeOffset.UnixEpoch : since);
        command.Parameters.Add(new NpgsqlParameter("model", NpgsqlDbType.Text) { Value = (object?)model ?? DBNull.Value });
        return Convert.ToDouble(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
