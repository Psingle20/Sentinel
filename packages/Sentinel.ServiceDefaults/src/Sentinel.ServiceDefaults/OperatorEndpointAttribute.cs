namespace Sentinel.ServiceDefaults;

/// <summary>
/// Marks a controller whose actions are operator endpoints: they exist only where
/// <see cref="OperatorEndpoints.AreEnabled"/> holds, and are not mapped at all elsewhere.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OperatorEndpointAttribute : Attribute;
