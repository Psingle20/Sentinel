using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

public sealed record KnowledgeResult(IReadOnlyList<KnowledgeDocument> Documents, string RedactionPolicyVersion);
