using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.Contracts.Messaging;
using Shouldly;

namespace Sentinel.Contracts.Tests;

/// <summary>
/// Compatibility snapshots: each message's JSON is committed under Snapshots/. A change to the
/// wire format fails here until the snapshot is regenerated deliberately (ADR-012 versioning).
/// Regenerate with the environment variable SENTINEL_UPDATE_SNAPSHOTS=1.
/// </summary>
public sealed class ContractSnapshotTests
{
    private static readonly JsonSerializerOptions Indented = new(SentinelJson.Options) { WriteIndented = true };

    public static TheoryData<string> MessageTypes
    {
        get
        {
            TheoryData<string> data = new();
            foreach (string messageType in ContractSamples.All.Keys)
            {
                data.Add(messageType);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Serialized_form_matches_the_committed_snapshot(string messageType)
    {
        object sample = ContractSamples.All[messageType];
        string json = JsonSerializer.Serialize(sample, sample.GetType(), Indented);
        string path = SnapshotPath(messageType);

        if (Environment.GetEnvironmentVariable("SENTINEL_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json + "\n");
        }

        File.Exists(path).ShouldBeTrue($"No snapshot for {messageType}. Run the tests with SENTINEL_UPDATE_SNAPSHOTS=1 to create it.");
        JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(File.ReadAllText(path)))
            .ShouldBeTrue($"{messageType} no longer matches its snapshot. Additive changes only within a schema version (ADR-012).");
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Snapshots_deserialize_and_round_trip_unchanged(string messageType)
    {
        Type type = ContractSamples.All[messageType].GetType();
        string snapshot = File.ReadAllText(SnapshotPath(messageType));

        object restored = JsonSerializer.Deserialize(snapshot, type, SentinelJson.Options)!;
        string reserialized = JsonSerializer.Serialize(restored, type, SentinelJson.Options);

        JsonNode.DeepEquals(JsonNode.Parse(reserialized), JsonNode.Parse(snapshot)).ShouldBeTrue();
    }

    private static string SnapshotPath(string messageType, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots", $"{messageType}.json");
}
