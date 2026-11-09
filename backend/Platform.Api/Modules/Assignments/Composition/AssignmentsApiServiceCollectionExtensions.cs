using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Modules.Assignments.Api;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Composition;

// ASN-04 / ASN-06 TIM 15 (celina 5)

public static class AssignmentsApiServiceCollectionExtensions
{
    /// <summary>
    /// Registruje API i audit deo Assignments modula. Pozvati POSLE <c>AddAudit()</c> i
    /// <c>AddAuditPersistence(...)</c> (daju <c>ICorrelationContext</c> i <c>ITrustedAuditWriter</c>).
    /// Portovi drugih članova se registruju sa TryAdd: prava implementacija registrovana pre ovog
    /// poziva ima prednost, a ona registrovana posle (Add/Replace) se razrešava kao poslednja.
    /// </summary>
    public static IServiceCollection AddAssignmentsApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        // Celina 5: audit adapter i API.
        services.TryAddScoped<IAssignmentAuditContextProvider, SessionAssignmentAuditContextProvider>();
        services.TryAddScoped<IAssignmentAuditWriter, AssignmentAuditWriter>();
        services.TryAddScoped<IDeadlineExtensionAuditWriter, DeadlineExtensionAuditAdapter>();
        services.TryAddScoped<AssignmentCaller>();
        services.TryAddScoped<StartAttemptApiHandler>();
        services.TryAddScoped<ExtendDeadlineApiHandler>();

        // Use-case-ovi (članovi 2 i 3); ovde se samo povezuju.
        services.TryAddScoped<StartAttemptService>();
        services.TryAddScoped<ExtendDeadlineService>();

        // Portovi članova 1 i 4: dok ne stignu pravi adapteri, sve se odbija (fail-closed).
        services.TryAddSingleton<IAssignmentRepository, NotConfiguredAssignmentRepository>();
        services.TryAddSingleton<IAttemptStore, NotConfiguredAttemptStore>();
        services.TryAddSingleton<IDeadlineExtensionStore, NotConfiguredDeadlineExtensionStore>();
        services.TryAddSingleton<IStartAttemptAuthorization, DenyAllStartAttemptAuthorization>();
        services.TryAddSingleton<IDeadlineExtensionAuthorization, DenyAllDeadlineExtensionAuthorization>();
        services.TryAddSingleton<ICourseEnrollmentCheck, NoActiveEnrollmentCheck>();

        // Eksplicitna prijava nepovezanih portova pri pokretanju (umesto tihog odbijanja).
        services.AddHostedService<AssignmentsCompositionCheck>();

        return services;
    }
}
