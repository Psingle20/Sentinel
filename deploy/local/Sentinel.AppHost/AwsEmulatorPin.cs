namespace Sentinel.AppHost;

/// <summary>The emulator ADR-021 pins, as <c>reference-system/deploy/aws/emulator.json</c> records it.</summary>
internal sealed record AwsEmulatorPin(string Emulator, string Image, string Version, string Platform, string Digest, int Port, string Decision);
