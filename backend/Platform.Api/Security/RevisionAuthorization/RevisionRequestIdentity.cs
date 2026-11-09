using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Interfaces;
using Platform.Identity.Enums;

namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionRequestIdentity
{
    // Separate from SessionCheckMiddleware's key: that middleware revalidates the
    // session before the endpoint and owns its own item.
    public const string SessionItemKey = "rev05.validated-session";

    public static bool AppliesTo(HttpContext http) => http.Request.Path.StartsWithSegments("/api/revisions");

    public static SubjectRef? Subject(ValidateSession? session) =>
        session is { Level: AuthenticationLevel.Active } && session.UserId != Guid.Empty
            ? new SubjectRef(session.UserId.ToString("D"), AccountState.Active) : null;

    /// <summary>Call after all Identity/Access registrations, before building the host.</summary>
    public static IServiceCollection AddRevisionRequestIdentity(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RevisionIdentityMarker)))
            return services;
        Decorate<IAccessSubjectProvider>(services, (provider, original) =>
            new RevisionSessionSubjectProvider(original, provider.GetRequiredService<ISessionManager>(),
                provider.GetRequiredService<IAccountStatusReader>()));
        Decorate<IRequestSubjectResolver>(services, (_, original) => new RevisionSubjectResolver(original));
        services.AddSingleton<RevisionIdentityMarker>();
        return services;
    }

    private sealed class RevisionIdentityMarker;

    private static void Decorate<T>(IServiceCollection services, Func<IServiceProvider, T, T> decorate) where T : class
    {
        var original = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(T))
            ?? throw new InvalidOperationException($"Register {typeof(T).Name} before REV-05 request identity.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(Original<T>), provider =>
            new Original<T>((T)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
                original.ImplementationInstance is null), original.Lifetime));
        services.AddScoped<T>(provider => decorate(provider, provider.GetRequiredService<Original<T>>().Value));
    }

    private sealed class Original<T>(T value, bool ownsValue) : IDisposable where T : class
    {
        public T Value => value;
        public void Dispose()
        {
            if (ownsValue) (value as IDisposable)?.Dispose();
        }
    }
}

/// <summary>Authenticates only revision routes using the public Identity session contract.</summary>
public sealed class RevisionSessionSubjectProvider(IAccessSubjectProvider inner, ISessionManager sessions,
    IAccountStatusReader accounts)
    : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(HttpContext http, CancellationToken cancellationToken = default)
    {
        if (!RevisionRequestIdentity.AppliesTo(http))
            return await inner.GetSubjectAsync(http, cancellationToken);

        http.Items.Remove(RevisionRequestIdentity.SessionItemKey);
        var token = http.Request.Cookies[SessionCookie.Name];
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var session = await sessions.ValidateAsync(token, cancellationToken);
        var subject = RevisionRequestIdentity.Subject(session);
        if (subject is not null &&
            (await accounts.FindAsync(session!.UserId, cancellationToken)) is not { Status: AccountStatus.Active })
            return null;
        if (subject is not null)
            http.Items[RevisionRequestIdentity.SessionItemKey] = session;
        return subject;
    }
}

/// <summary>Uses the same confirmed identity as the Access middleware on revision routes.</summary>
public sealed class RevisionSubjectResolver(IRequestSubjectResolver inner) : IRequestSubjectResolver
{
    public SubjectRef? Resolve(HttpContext http)
    {
        if (!RevisionRequestIdentity.AppliesTo(http))
            return inner.Resolve(http);

        return RevisionRequestIdentity.Subject(
            http.Items[Platform.Api.Middleware.SessionCheckMiddleware.SessionItemKey] as ValidateSession);
    }
}
