using System.Globalization;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Infrastructure.Persistence;

/// <summary>An opaque keyset cursor: the last row's event time and ID.</summary>
internal static class ContinuationToken
{
    public static string Create(DateTimeOffset eventTime, string observationId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{eventTime.UtcTicks}|{observationId}")));

    public static bool TryParse(string? token, out DateTimeOffset eventTime, out string? observationId)
    {
        eventTime = default;
        observationId = null;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            string[] parts = Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
            {
                return false;
            }

            eventTime = new DateTimeOffset(ticks, TimeSpan.Zero);
            observationId = parts[1];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
