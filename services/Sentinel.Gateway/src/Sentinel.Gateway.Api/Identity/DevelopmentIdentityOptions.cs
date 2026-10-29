using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Sentinel.Gateway.Api.Identity;

/// <summary>Configured as <c>Gateway:DevIdentity</c>. Users are synthetic addresses on the reserved <c>example.test</c> domain.</summary>
public sealed class DevelopmentIdentityOptions
{
    public const string Section = "Gateway:DevIdentity";

    public bool Enabled { get; set; }

    public string DefaultUser { get; set; } = string.Empty;

    public IDictionary<string, string[]> Users { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tenants every development identity may see.</summary>
    public IList<string> Tenants { get; } = [];
}
