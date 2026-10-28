using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks;

public interface IDependencyModule
{
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
