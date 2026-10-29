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

public sealed record RevisionView(string Service, string Revision, DateTimeOffset ActiveSince, IReadOnlyCollection<string> KnownRevisions);
