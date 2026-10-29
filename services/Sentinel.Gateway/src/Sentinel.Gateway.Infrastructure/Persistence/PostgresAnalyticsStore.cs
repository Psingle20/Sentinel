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

/// <summary>Daily counts per event and property values; individual events are never stored.</summary>
internal sealed class PostgresAnalyticsStore(NpgsqlDataSource dataSource) : IAnalyticsStore
{
    public async Task IncrementAsync(DateOnly day, string eventName, string dimensions, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            insert into api.analytics_daily (day, event, dimensions, count) values (@day, @event, @dimensions, 1)
            on conflict (day, event, dimensions) do update set count = api.analytics_daily.count + 1
            """);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("event", eventName);
        command.Parameters.AddWithValue("dimensions", dimensions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsCount>> SinceAsync(DateOnly from, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select day, event, dimensions, count from api.analytics_daily where day >= @from order by day desc, event, dimensions");
        command.Parameters.AddWithValue("from", from);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<AnalyticsCount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new AnalyticsCount(await reader.GetFieldValueAsync<DateOnly>(0, cancellationToken), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        }

        return counts;
    }
}
