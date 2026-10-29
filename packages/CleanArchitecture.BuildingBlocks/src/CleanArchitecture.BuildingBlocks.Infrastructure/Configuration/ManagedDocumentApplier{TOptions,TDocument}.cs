using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

internal sealed class ManagedDocumentApplier<TOptions, TDocument>(ManagedDocument<TDocument> document, Action<TOptions, TDocument> apply) : IPostConfigureOptions<TOptions>
    where TOptions : class
    where TDocument : class
{
    public void PostConfigure(string? name, TOptions options)
    {
        if (name == Options.DefaultName && document.Current is { } current)
        {
            apply(options, current.Document);
        }
    }
}
