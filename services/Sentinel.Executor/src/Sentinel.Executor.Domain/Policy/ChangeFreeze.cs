using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>A change freeze: nothing in the listed environments runs while it lasts.</summary>
public sealed record ChangeFreeze(string Id, IReadOnlyList<string> Environments, DateTimeOffset From, DateTimeOffset Until, string Reason);
