using CleanArchitecture.BuildingBlocks.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;

internal sealed class ManagedDocumentChangeTokenSource<TOptions, TDocument>(ManagedDocument<TDocument> document) : IOptionsChangeTokenSource<TOptions>
    where TDocument : class
{
    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => document.GetChangeToken();
}
