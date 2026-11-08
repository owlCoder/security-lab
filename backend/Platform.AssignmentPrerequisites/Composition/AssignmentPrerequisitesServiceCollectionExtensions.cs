using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.AssignmentPrerequisites.Application;
using Platform.AssignmentPrerequisites.Contracts;
using Platform.AssignmentPrerequisites.Infrastructure.Persistence;

namespace Platform.AssignmentPrerequisites.Composition;

public static class AssignmentPrerequisitesServiceCollectionExtensions
{
    public static IServiceCollection AddAssignmentPrerequisites(
        this IServiceCollection services,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:Assignments is required for assignment prerequisites (ASN-07).");
        }

        services.AddDbContext<AssignmentPrerequisitesDbContext>(options =>
            options.UseAssignmentPrerequisitesNpgsql(connectionString));
        services.AddScoped<IAssignmentPrerequisiteStore, PostgresAssignmentPrerequisiteStore>();
        services.AddScoped<AssignmentPrerequisiteCatalog>();

        return services;
    }
}
