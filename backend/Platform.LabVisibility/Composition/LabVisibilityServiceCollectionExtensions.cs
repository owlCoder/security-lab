using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.LabVisibility.Application.EvaluatorKeys;
using Platform.LabVisibility.Application.Preview;
using Platform.LabVisibility.Application.StudentView;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Auditing;
using Platform.LabVisibility.Infrastructure.Membership;
using Platform.LabVisibility.Infrastructure.Persistence;
using Platform.LabVisibility.Infrastructure.Policies;
using Platform.LabVisibility.Infrastructure.Publication;

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

        // KT2 ugovori (docs/contracts/LabVisibility-KT2.md):
        // upis ključa (Gajić, kartica 5) i stanje objave od AUT-01/REV (stub do njihovog adaptera).
        services.AddScoped<ISaveEvaluatorKey, SaveEvaluatorKey>();
        services.AddSingleton<StubLabPublicationSource>();
        services.AddSingleton<ILabPublicationSource>(
            sp => sp.GetRequiredService<StubLabPublicationSource>());

        return services;
    }

    // #185 (strogi ACC-05): katalog i politike se zamrzavaju u AddAccessControl(), pa se LabVisibility
    // radnje prijavljuju pre toga. Istu metodu koriste Program.cs i testovi (kao AddRevisionAccessPolicies, #192).
    public static IServiceCollection AddLabVisibilityAccess(
        this IServiceCollection services)
    {
        // Ulazna radnja na HTTP rutama /student-view i /preview: middleware traži samo aktivnu sesiju.
        services.AddAccessAction<LabVisibilityEntryPolicy>(
            new ActionDefinition(
                LabVisibilityActions.Entry,
                ActionScope.Authenticated,
                null,
                false));

        // AUT-04: studentski prikaz objavljene laboratorije (Stojanović).
        services.AddAccessAction<StudentViewPolicy>(
            new ActionDefinition(
                LabVisibilityActions.StudentView,
                ActionScope.Course,
                "lab",
                false));

        // AUT-07: pregled pre objave samo za nastavnika kursa (Cvetojević).
        services.AddAccessAction<LabPreviewPolicy>(
            new ActionDefinition(
                LabVisibilityActions.Preview,
                ActionScope.Course,
                "lab",
                false));

        // AUT-04: evaluator ključ (Kovačević).
        services.AddAccessAction<EvaluatorKeyPolicy>(
            new ActionDefinition(
                LabVisibilityActions.KeyRead,
                ActionScope.Course,
                "lab",
                true));

        // KT2: upis evaluator ključa (Gajić, kartica 5). Do tada politika odbija sve.
        services.AddAccessAction<EvaluatorKeyWritePolicy>(
            new ActionDefinition(
                LabVisibilityActions.KeyWrite,
                ActionScope.Course,
                "lab",
                true));

        return services;
    }
}
