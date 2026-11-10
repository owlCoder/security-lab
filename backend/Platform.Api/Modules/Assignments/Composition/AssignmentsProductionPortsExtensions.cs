using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Platform.Access.Contracts;
using Platform.Access.Infrastructure.Resources;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Api.Modules.Assignments.Integration;
using Platform.Api.Modules.Assignments.Persistence;
using Platform.Assignments.Contracts;
using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Composition;

// ASN-04 / ASN-06 TIM 15 (celina 5): produkciono povezivanje portova članova 1 i 4.
//
// AddAssignmentsApi() i dalje registruje fail-closed podrazumevane implementacije (TryAdd). Ove metode
// ih ZAMENJUJU stvarnim adapterima, nezavisno od redosleda poziva (Replace), pa:
//   - autorizacija ide kroz centralni IAccessDecision uvek (bez konfiguracije; bez članstva sve se odbija);
//   - skladišta su PostgreSQL čim postoji ConnectionStrings:Assignments; bez nje ostaju fail-closed i
//     AssignmentsCompositionCheck to prijavljuje (Error van Development okruženja).

public static class AssignmentsProductionPortsExtensions
{
    /// <summary>
    /// Portovi člana 4 nad Access modulom (ACC-05). Pozvati POSLE <c>AddAccessControl()</c> (da bi
    /// postojeći <see cref="IResourceDescriptorResolver"/> bio dekorisan, a ne zamenjen).
    /// </summary>
    public static IServiceCollection AddAssignmentsAccessAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AssignmentAccessGate>();
        services.Replace(ServiceDescriptor.Scoped<IStartAttemptAuthorization, AccessDecisionStartAttemptAuthorization>());
        services.Replace(ServiceDescriptor.Scoped<IDeadlineExtensionAuthorization, AccessDecisionDeadlineExtensionAuthorization>());
        services.Replace(ServiceDescriptor.Scoped<ICourseEnrollmentCheck, MembershipCourseEnrollmentCheck>());

        DecorateResourceResolver(services);
        return services;
    }

    /// <summary>
    /// Portovi člana 1 nad PostgreSQL-om (šema <c>assignments</c>). Za uspešan ASN-06 audit u istoj
    /// transakciji audit šema treba da je u istoj bazi; ako nije, zapis ostaje u outbox-u (nikad se ne gubi).
    /// Šemu primenjuje <see cref="AssignmentsDatabase.MigrateAsync"/> (deploy korak; u Development-u Program.cs).
    /// </summary>
    public static IServiceCollection AddAssignmentsPostgres(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<AssignmentsDataSource>(_ =>
            new AssignmentsDataSource(NpgsqlDataSource.Create(connectionString))));
        services.Replace(ServiceDescriptor.Singleton<IAssignmentRepository>(sp =>
            new PostgresAssignmentRepository(sp.GetRequiredService<AssignmentsDataSource>().Value)));
        services.Replace(ServiceDescriptor.Singleton<IAttemptStore>(sp =>
            new PostgresAttemptStore(
                sp.GetRequiredService<AssignmentsDataSource>().Value,
                sp.GetRequiredService<TimeProvider>())));
        services.Replace(ServiceDescriptor.Scoped<IDeadlineExtensionStore>(sp =>
            new PostgresDeadlineExtensionStore(
                sp.GetRequiredService<AssignmentsDataSource>().Value,
                sp.GetRequiredService<ITransactionalAuditWriter>(),
                sp.GetRequiredService<IAssignmentAuditContextProvider>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<PostgresDeadlineExtensionStore>>())));

        return services;
    }

    // Access registar (singleton) dobija JEDAN IResourceDescriptorResolver. Zato se postojeći ne
    // briše nego omotava: tip "assignment" rešava naš resolver, sve ostalo i dalje prethodni.
    private static void DecorateResourceResolver(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AssignmentsResolverMarker)))
        {
            return; // već dekorisano; drugi poziv ne pravi dvostruki omotač
        }

        var previous = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IResourceDescriptorResolver) && !descriptor.IsKeyedService);

        services.RemoveAll<IResourceDescriptorResolver>();
        services.AddSingleton<IResourceDescriptorResolver>(sp =>
            new AssignmentResourceDescriptorResolver(
                sp.GetRequiredService<IServiceScopeFactory>(),
                CreateInner(sp, previous),
                sp.GetRequiredService<ILogger<AssignmentResourceDescriptorResolver>>()));
        services.AddSingleton<AssignmentsResolverMarker>();
    }

    private static IResourceDescriptorResolver CreateInner(IServiceProvider services, ServiceDescriptor? previous)
    {
        if (previous is null)
        {
            return new EmptyResourceDescriptorResolver();
        }

        if (previous.ImplementationInstance is IResourceDescriptorResolver instance)
        {
            return instance;
        }

        if (previous.ImplementationFactory is not null)
        {
            return (IResourceDescriptorResolver)previous.ImplementationFactory(services);
        }

        return previous.ImplementationType is not null
            ? (IResourceDescriptorResolver)ActivatorUtilities.CreateInstance(services, previous.ImplementationType)
            : new EmptyResourceDescriptorResolver();
    }

    /// <summary>Oznaka da je resolver već dekorisan (višestruki poziv ne pravi dvostruki omotač).</summary>
    internal sealed class AssignmentsResolverMarker
    {
    }
}

/// <summary>
/// Sopstveni <see cref="NpgsqlDataSource"/> Assignments modula, umotan da se ne sudari sa eventualnim
/// data source-om drugog modula u DI kontejneru. Kontejner ga pravi (factory), pa ga i oslobađa pri gašenju hosta.
/// </summary>
public sealed class AssignmentsDataSource(NpgsqlDataSource value) : IAsyncDisposable, IDisposable
{
    public NpgsqlDataSource Value { get; } = value;

    public ValueTask DisposeAsync() => Value.DisposeAsync();

    public void Dispose() => Value.Dispose();
}
