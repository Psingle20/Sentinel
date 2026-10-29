using CleanArchitecture.BuildingBlocks;
using Microsoft.AspNetCore.Mvc;
using Sentinel.ServiceDefaults;

namespace Sentinel.Gateway.Api;

/// <summary>
/// What only the Gateway process needs (ADR-020): controllers with the contract JSON rules,
/// identity, and OpenAPI. Responses keep their minimal-API shapes: actions return the same
/// <see cref="IResult"/>s, status-code results are not rewritten into problem documents, and a
/// non-nullable property is not an implicit required-field rule.
/// </summary>
public sealed class ApiDependencyModule(IHostEnvironment environment) : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddJsonOptions(options => HttpConventions.Apply(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.SuppressMapClientErrors = true);
        services.AddOpenApi();
        services.AddSentinelIdentity(configuration, environment);
    }
}