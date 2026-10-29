using System.Text.Json;

namespace Sentinel.Contracts.Remediation;

/// <summary>What an operator attestation for a policy publication hashes: the document and the version it replaces; the actor comes from the claims.</summary>
public sealed record PolicyPublicationContent(JsonElement Document, string ExpectedVersion);
