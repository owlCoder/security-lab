using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Platform.Access.AspNet;

public static class AccessAspNetExtensions
{
    public static IServiceCollection AddAccessAspNet(
        this IServiceCollection services)
    {
        services.TryAddScoped<
            IAccessSubjectProvider,
            AnonymousAccessSubjectProvider>();
            
        return services;
    }

    public static IApplicationBuilder UseAccessControl(
        this IApplicationBuilder app)
    {
        return app.UseMiddleware<AccessControlMiddleware>();
    }
}