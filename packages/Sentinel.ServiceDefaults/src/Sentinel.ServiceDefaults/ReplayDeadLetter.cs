using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sentinel.ServiceDefaults;

public sealed record ReplayDeadLetter(string Stream, string EntryId);
