using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.Abstractions;

/// <summary>The seeded checkout traffic every scenario runs under.</summary>
public interface ITraffic
{
    TrafficSettings Settings { get; }

    void Update(TrafficSettings settings);
}
