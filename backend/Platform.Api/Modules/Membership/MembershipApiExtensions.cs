using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Membership.Application;
using Platform.Membership.Composition;
using Platform.Membership.Contracts;

namespace Platform.Api.Modules.Membership;

/// <summary>
/// ACC-02: jedino mesto gde se modul za članstvo povezuje sa Platform.Api.
/// Program.cs poziva samo AddMembership() i MapMembershipAsync(); sve ostalo je ovde,
/// u našem folderu, pa zajednički fajlovi ostaju netaknuti kada menjamo svoj kod.
/// </summary>
public static class MembershipApiExtensions
{
    /// <summary>Poziva se pre AddAccessControl() (strogi ACC-05).</summary>
    public static IServiceCollection AddMembership(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        services.AddMembershipModule(environment);

        // Status naloga iz Identity modula (IDN-03). Scoped, jer Identity citac koristi DbContext.
        services.AddScoped<IAccountStatusReader, IdentityAccountStatusReader>();

        // ACC-02-4 / ACC-05: radnja upisa i njena politika se prijavljuju ovde, pre AddAccessControl(),
        // jer Access posle toga zamrzava katalog radnji i registar politika (isto kao AddLabVisibilityAccess).
        services.AddAccessAction<EnrollCourseMemberPolicy>(
            new ActionDefinition(
                MembershipActions.EnrollMember,
                ActionScope.Course,
                MembershipActions.CourseResourceType,
                IsPrivileged: true));

        return services;
    }

    public static async Task MapMembershipAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // ACC-02-3: POST /api/courses/{courseId}/members
        app.MapMembershipEndpoints();

        // Samo lokalno: probni nastavnici u probnim kursevima (ako je baza podesena).
        if (app.Environment.IsDevelopment())
        {
            await app.Services.SeedMembershipDevelopmentDataAsync();
        }
    }
}