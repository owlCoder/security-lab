using Platform.Access.AspNet;
using Platform.Api.Middleware;
using Team21.Acc.Api;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;

namespace Platform.Api.Modules.Team21.Integration;

/// <summary>Host composition limited to server-marked Team 21 endpoints.</summary>
public static class Team21SessionAccessExtensions
{
    private static readonly object InnerProviderKey = new();

    public static IServiceCollection AddTeam21SessionAccess(this IServiceCollection services)
    {
        // A keyed copy preserves the original provider's lifetime, activation and disposal.
        if (services.Any(d => d.IsKeyedService && ReferenceEquals(d.ServiceKey, InnerProviderKey)))
            return services;

        var original = services.LastOrDefault(d =>
            d.ServiceType == typeof(IAccessSubjectProvider) && !d.IsKeyedService)
            ?? throw new InvalidOperationException("Register the existing Access subject provider first.");

        ServiceDescriptor preserved;
        if (original.ImplementationInstance is { } instance)
            preserved = ServiceDescriptor.KeyedSingleton(typeof(IAccessSubjectProvider), InnerProviderKey, instance);
        else if (original.ImplementationFactory is { } factory)
            preserved = ServiceDescriptor.DescribeKeyed(typeof(IAccessSubjectProvider), InnerProviderKey,
                (provider, _) => factory(provider), original.Lifetime);
        else
            preserved = ServiceDescriptor.DescribeKeyed(typeof(IAccessSubjectProvider), InnerProviderKey,
                original.ImplementationType!, original.Lifetime);

        services.Remove(original);
        services.Add(preserved);
        services.AddScoped<IAccessSubjectProvider>(provider => new Team21AccessSubjectProvider(
            provider.GetRequiredKeyedService<IAccessSubjectProvider>(InnerProviderKey),
            () => provider.GetRequiredService<ICurrentActor>(),
            () => provider.GetRequiredService<IAccountStatusReader>()));
        return services;
    }

    public static IApplicationBuilder UseTeam21SessionBeforeAccess(this IApplicationBuilder app) =>
        app.UseWhen(IsTeam21Endpoint, branch => branch.UseMiddleware<SessionCheckMiddleware>());

    public static IApplicationBuilder UseSessionForOtherEndpoints(this IApplicationBuilder app) =>
        app.UseWhen(context => !IsTeam21Endpoint(context),
            branch => branch.UseMiddleware<SessionCheckMiddleware>());

    private static bool IsTeam21Endpoint(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<Team21SessionRequiredMetadata>() is not null;
}
