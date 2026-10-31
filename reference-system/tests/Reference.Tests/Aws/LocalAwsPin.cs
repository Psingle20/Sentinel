namespace Reference.Tests.Aws;

/// <summary>Local AWS image pinned in <c>reference-system/deploy/aws/local-aws.json</c>.</summary>
public sealed record LocalAwsPin(string Tool, string Image, string Version, string Platform, string Digest, int Port);
