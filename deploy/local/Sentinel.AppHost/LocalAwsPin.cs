namespace Sentinel.AppHost;

/// <summary>Local AWS ADR-021 pins, as <c>reference-system/deploy/aws/local-aws.json</c> records it.</summary>
internal sealed record LocalAwsPin(string Tool, string Image, string Version, string Platform, string Digest, int Port, string Decision);
