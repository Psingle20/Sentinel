using System.Text.Json;
using System.Text.Json.Serialization;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Api.Manifests;

/// <summary>Reads every manifest in a directory, in name order, into the catalog, which refuses any that break its rules.</summary>
public static class ScenarioCatalogLoader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ScenarioCatalog Load(string directory) =>
        ScenarioCatalog.From([.. Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Select(path => (
            Path.GetFileName(path),
            JsonSerializer.Deserialize<ScenarioManifest>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path} is empty.")))]);
}
