using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

internal sealed class CurrentOptions<TOptions>(IOptionsMonitor<TOptions> monitor) : IOptions<TOptions>
    where TOptions : class
{
    public TOptions Value => monitor.CurrentValue;
}
