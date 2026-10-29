using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Reference.Common.Revisions;

namespace Reference.Common.Control;

/// <summary>
/// Guards the endpoints that change behaviour: revision switches, batch control and reset. They
/// exist only in the Development or Test environment with <c>Reference:FailureControls:Enabled</c>
/// set, and every call must present the control token (Phase 01 safety and isolation).
/// </summary>
public sealed class FailureControl
{
    public const string TokenHeader = "X-Reference-Control-Token";

    private readonly byte[] _token;

    private FailureControl(bool enabled, string? token)
    {
        Enabled = enabled;
        _token = Encoding.UTF8.GetBytes(token ?? string.Empty);
    }

    public bool Enabled { get; }

    public static FailureControl From(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        bool allowedEnvironment = environment.IsDevelopment() || environment.IsEnvironment("Test");
        bool requested = configuration.GetValue<bool>("Reference:FailureControls:Enabled");
        string? token = configuration["Reference:ControlToken"];

        if (requested && allowedEnvironment && string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Reference:ControlToken is required when failure controls are enabled.");
        }

        return new FailureControl(requested && allowedEnvironment, token);
    }

    public bool IsAuthorized(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enabled || !request.Headers.TryGetValue(TokenHeader, out StringValues presented))
        {
            return false;
        }

        byte[] candidate = Encoding.UTF8.GetBytes(presented.ToString());
        return CryptographicOperations.FixedTimeEquals(candidate, _token);
    }
}
