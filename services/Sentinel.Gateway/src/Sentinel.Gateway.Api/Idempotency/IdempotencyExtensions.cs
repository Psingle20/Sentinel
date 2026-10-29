using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sentinel.Gateway.Application.Idempotency;

namespace Sentinel.Gateway.Api.Idempotency;

public static class IdempotencyExtensions
{
    public const string Header = "Idempotency-Key";

    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Requires and honours <c>Idempotency-Key</c> on the public API's mutations; place it after authorization.</summary>
    public static IApplicationBuilder UseIdempotencyKeys(this IApplicationBuilder app) => app.UseMiddleware<IdempotencyMiddleware>();
}
