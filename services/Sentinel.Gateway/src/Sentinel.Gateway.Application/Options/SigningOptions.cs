namespace Sentinel.Gateway.Application.Options;

/// <summary>Configured as <c>Gateway:Signing</c>.</summary>
public sealed class SigningOptions
{
    public const string Section = "Gateway:Signing";

    /// <summary>A new signing key is generated after this long; the previous one stays published until its attestations expire.</summary>
    public TimeSpan RotateAfter { get; set; } = TimeSpan.FromHours(12);
}
