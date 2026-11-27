using Platform.Access.AspNet;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Competencies;

public static class CompetencyRequestIdentity
{
    // Match metadata rather than the shared /api/laboratories URL prefix.
    public static bool AppliesTo(HttpContext http) =>
        http.GetEndpoint()?.Metadata.GetMetadata<IAccessRequirementMetadata>()?.Action
            == CompetencyLinkActions.Entry;

    // Call after shared Access/Identity registrations, before building the host.
    public static IServiceCollection AddPrg04RequestIdentity(this IServiceCollection services)
    {
        var original = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IAccessSubjectProvider) && !descriptor.IsKeyedService)
            ?? throw new InvalidOperationException("Register the platform subject provider before PRG-04 identity.");
        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(OriginalProvider), provider => new OriginalProvider(
            (IAccessSubjectProvider)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider) ??
                ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
            original.ImplementationInstance is null), original.Lifetime));
        services.AddScoped<IAccessSubjectProvider>(provider => new CompetencySubjectProvider(
            provider.GetRequiredService<OriginalProvider>().Value,
            provider.GetRequiredService<Prg04SessionIdentity>()));
        return services;
    }

    private sealed class OriginalProvider(IAccessSubjectProvider value, bool ownsValue) : IDisposable
    {
        public IAccessSubjectProvider Value => value;
        public void Dispose() { if (ownsValue) (Value as IDisposable)?.Dispose(); }
    }
}

public sealed class CompetencySubjectProvider(IAccessSubjectProvider inner, Prg04SessionIdentity identity)
    : IAccessSubjectProvider
{
    public async ValueTask<SubjectRef?> GetSubjectAsync(HttpContext http,
        CancellationToken cancellationToken = default)
    {
        if (!CompetencyRequestIdentity.AppliesTo(http))
            return await inner.GetSubjectAsync(http, cancellationToken);

        // Reuse the handlers' exact session, nonempty handle and active-account checks.
        var userId = await identity.ResolveAsync(http, cancellationToken);
        return userId is { } id ? new SubjectRef(id.ToString("D"), AccountState.Active) : null;
    }
}
