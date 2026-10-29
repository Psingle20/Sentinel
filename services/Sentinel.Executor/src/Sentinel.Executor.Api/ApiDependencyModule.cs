using CleanArchitecture.BuildingBlocks;
using Sentinel.ServiceDefaults;

namespace Sentinel.Executor.Api;

/// <summary>
/// What only the Executor process needs (ADR-020): controllers with the contract JSON rules, and the
/// operator controllers only where operator endpoints are enabled. Responses keep their minimal-API
/// shapes: actions return the same <see cref="IResult"/>s, status-code results are not rewritten,
/// and a non-nullable property is not an implicit required-field rule.
/// </summary>
public sealed class ApiDependencyModule(IHostEnvironment environment) : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        bool operatorEndpoints = OperatorEndpoints.AreEnabled(environment, configuration);
        services.AddControllers(options =>
            {
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.Conventions.Add(new OperatorEndpointConvention(operatorEndpoints));
            })
            .AddJsonOptions(options => HttpConventions.Apply(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.SuppressMapClientErrors = true);
    }
}
