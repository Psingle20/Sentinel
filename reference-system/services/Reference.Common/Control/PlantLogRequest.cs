namespace Reference.Common.Control;

/// <summary>A log line to plant and how long the service keeps logging it.</summary>
public sealed record PlantLogRequest(string Message, int Seconds);
