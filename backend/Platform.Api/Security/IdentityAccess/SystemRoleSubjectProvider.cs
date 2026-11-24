using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Microsoft.Extensions.Logging;

namespace Platform.Api.Security.IdentityAccess;

public sealed class SystemRoleSubjectProvider(
    IAccessSubjectProvider inner,
    IAdminDirectory adminDirectory,
    ILogger<SystemRoleSubjectProvider> logger) : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var subject = await inner.GetSubjectAsync(httpContext, cancellationToken);
        if (subject is null)
            return null;

        SystemRole role = SystemRole.None;
        if (Guid.TryParseExact(subject.Id, "D", out var subjectId))
        {
            try
            {
                if (adminDirectory.IsAdministrator(subjectId))
                    role = SystemRole.Administrator;
            }
            catch (Exception exception)
            {
                role = SystemRole.None;
                logger.LogWarning(
                    "Administrator directory lookup failed with exception type {ExceptionType}.",
                    exception.GetType().Name);
            }
        }

        return subject with { SystemRole = role };
    }
}

public static class SystemRoleSubjectProviderRegistration
{
    public static IServiceCollection AddSystemRoleSubjectProvider(this IServiceCollection services)
    {
        var original = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(IAccessSubjectProvider) &&
                          !descriptor.IsKeyedService)
            ?? throw new InvalidOperationException(
                "Register the final IAccessSubjectProvider before adding SystemRole decoration.");

        services.Remove(original);
        services.AddScoped<IAccessSubjectProvider>(provider =>
        {
            IAccessSubjectProvider inner = original switch
            {
                { ImplementationInstance: IAccessSubjectProvider instance } => instance,
                { ImplementationFactory: { } factory } =>
                    (IAccessSubjectProvider)factory(provider),
                _ => (IAccessSubjectProvider)ActivatorUtilities.CreateInstance(
                    provider, original.ImplementationType!)
            };

            return new SystemRoleSubjectProvider(
                inner,
                provider.GetRequiredService<IAdminDirectory>(),
                provider.GetRequiredService<ILogger<SystemRoleSubjectProvider>>());
        });

        return services;
    }
}
