using Platform.AssignmentOverview.Application;
using Platform.AssignmentOverview.Contracts;
using Platform.AssignmentOverview.Infrastructure;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.MyAssignments;

public static class MyAssignmentsModule
{
    /// <summary>
    /// ASN-02: servis za listu dodela i njegove zavisnosti. Dok moduli za upise i dodele nemaju
    /// implementaciju, Development koristi privremene podatke, a ostala okruzenja fail-closed varijante.
    /// </summary>
    public static IServiceCollection AddMyAssignments(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddScoped<IMyAssignmentsService, MyAssignmentsService>();

        // ASN-07: prikaz „zaključano“ u listi dodela.
        services.AddScoped<AssignmentLockService>();

        if (environment.IsDevelopment())
        {
            services.AddSingleton<IStudentCourseReader, DevelopmentStudentCourseReader>();
            services.AddSingleton<IAssignmentRepository, DevelopmentAssignmentRepository>();

            // ASN-07: privremeni preduslovi i pokušaji.
            services.AddSingleton<IAssignmentPrerequisiteStore>(_ => DevelopmentLockData.CreatePrerequisiteStore());
            services.AddSingleton<IAttemptOutcomeReader, DevelopmentAttemptOutcomeReader>();
        }
        else
        {
            services.AddSingleton<IStudentCourseReader, UnavailableStudentCourseReader>();
            services.AddSingleton<IAssignmentRepository, UnavailableAssignmentRepository>();

            // ASN-07: fail-closed dok se ne povežu pravi moduli.
            services.AddSingleton<IAssignmentPrerequisiteStore, UnavailableAssignmentPrerequisiteStore>();
            services.AddSingleton<IAttemptOutcomeReader, UnavailableAttemptOutcomeReader>();
        }

        return services;
    }
}