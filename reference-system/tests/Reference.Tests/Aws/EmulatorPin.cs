namespace Reference.Tests.Aws;

/// <summary>The emulator image pinned in <c>reference-system/deploy/aws/emulator.json</c>.</summary>
public sealed record EmulatorPin(string Emulator, string Image, string Version, string Platform, string Digest, int Port);
