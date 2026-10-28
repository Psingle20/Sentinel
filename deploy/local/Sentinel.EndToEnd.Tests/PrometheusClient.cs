using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sentinel.EndToEnd.Tests;

/// <summary>Instant queries against the reference Prometheus: the scenario ground truth.</summary>
public sealed class PrometheusClient(HttpClient http) : IDisposable
{
    /// <summary>The first sample of an instant query, or null when the query returns nothing.</summary>
    public async Task<double?> ScalarAsync(string query, DateTimeOffset? at = null)
    {
        string time = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using JsonDocument document = (await http.GetFromJsonAsync<JsonDocument>($"/api/v1/query?query={Uri.EscapeDataString(query)}&time={time}"))!;
        JsonElement result = document.RootElement.GetProperty("data").GetProperty("result");
        if (result.GetArrayLength() == 0)
        {
            return null;
        }

        string value = result[0].GetProperty("value")[1].GetString()!;
        return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Every series of an instant query as <c>label=value</c> pairs, for diagnostics.</summary>
    public async Task<string> VectorAsync(string query, string label)
    {
        using JsonDocument document = (await http.GetFromJsonAsync<JsonDocument>($"/api/v1/query?query={Uri.EscapeDataString(query)}"))!;
        return string.Join(", ", document.RootElement.GetProperty("data").GetProperty("result").EnumerateArray()
            .Select(series => $"{(series.GetProperty("metric").TryGetProperty(label, out JsonElement name) ? name.GetString() : "?")}={series.GetProperty("value")[1].GetString()}"));
    }

    public void Dispose() => http.Dispose();
}
