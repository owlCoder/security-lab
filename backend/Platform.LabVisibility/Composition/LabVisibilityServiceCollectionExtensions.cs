using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Application.Preview;
using Platform.LabVisibility.Application.StudentView;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Policies;

namespace Platform.LabVisibility.Composition;

public static class LabVisibilityServiceCollectionExtensions
{
    public static IServiceCollection AddLabVisibility(
        this IServiceCollection services)
    {
        // PostgreSQL se koristi kada je podešena konekcija.
        // EF reader i store koriste fabriku DbContext-a.
        services.AddSingleton<InMemoryLabPublicContentReader>();
        services.AddSingleton<InMemoryEvaluatorKeyStore>();

        services.AddDbContextFactory<LabVisibilityDbContext>((sp, options) =>
            options.UseNpgsql(
                LabVisibilityDatabase.GetConnectionString(sp),
                LabVisibilityDesignTimeDbContextFactory.Configure));

        services.AddSingleton<EfLabPublicContentReader>();
        services.AddSingleton<EfEvaluatorKeyStore>();

        services.AddSingleton<ILabPublicContentReader>(sp =>
        {
            if (LabVisibilityDatabase.IsConfigured(sp))
            {
                return sp.GetRequiredService<EfLabPublicContentReader>();
            }

            return sp.GetRequiredService<InMemoryLabPublicContentReader>();
        });

        services.AddSingleton<IEvaluatorKeyStore>(sp =>
        {
            if (LabVisibilityDatabase.IsConfigured(sp))
            {
                return sp.GetRequiredService<EfEvaluatorKeyStore>();
            }

            return sp.GetRequiredService<InMemoryEvaluatorKeyStore>();
        });

        services.AddSingleton<InMemoryCourseMembership>();
        services.AddSingleton<ICourseMembership>(
            sp => sp.GetRequiredService<InMemoryCourseMembership>());

        services.AddLogging();
        services.AddSingleton<ILabVisibilityAudit, LoggerLabVisibilityAudit>();
        services.AddSingleton<EvaluatorKeyReader>();

        services.AddScoped<GetStudentLabView>();

        // AUT-07: pregled pre objave (Cvetojević)
        services.AddScoped<PreviewStudentView>();

        return services;
    }

    public static IServiceProvider RegisterLabVisibilityAccess(
        this IServiceProvider services)
    {
        var catalog = services.GetRequiredService<InMemoryActionCatalog>();

        foreach (var action in LabVisibilityActions.All)
        {
            var scope = ActionScope.Authenticated;
            string? resourceType = null;
            var requiresResource = false;

            // lab.key.read, lab.student-view i lab.preview (AUT-07) nose resurs "lab" (labId iz rute).
            if (action == LabVisibilityActions.KeyRead ||
                action == LabVisibilityActions.StudentView ||
                action == LabVisibilityActions.Preview)
            {
                scope = ActionScope.Course;
                resourceType = "lab";
                requiresResource = true;
            }

            catalog.Register(new ActionDefinition(
                action,
                scope,
                resourceType,
                requiresResource));
        }

        var policies = services.GetRequiredService<InMemoryAccessPolicyRegistry>();

        policies.Register(
            LabVisibilityActions.KeyRead,
            ActivatorUtilities.CreateInstance<EvaluatorKeyPolicy>(services));

        policies.Register(
            LabVisibilityActions.StudentView,
            ActivatorUtilities.CreateInstance<StudentViewPolicy>(services));

        // AUT-07: pregled pre objave samo za nastavnika kursa (Cvetojević)
        policies.Register(
            LabVisibilityActions.Preview,
            ActivatorUtilities.CreateInstance<LabPreviewPolicy>(services));

        return services;
    }
}