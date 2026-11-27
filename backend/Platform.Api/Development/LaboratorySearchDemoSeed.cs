using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Api.Development;

// LAB-05 demo koristi postojece ugovore i clanove demo kursa; ne menja politike pristupa.
public static class LaboratorySearchDemoSeed
{
    public const string EnabledSetting = "LabSearchDemo:Enabled";

    public static async Task SeedLaboratorySearchDemoAsync(
        this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        if (!services.GetRequiredService<IHostEnvironment>().IsDevelopment()
            || !services.GetRequiredService<IConfiguration>().GetValue<bool>(EnabledSetting))
        {
            return;
        }

        using IServiceScope scope = services.CreateScope();
        ILaboratoryRepository repository = scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>();
        if (repository is not InMemoryLaboratoryRepository)
        {
            return;
        }

        DateTimeOffset createdAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var drafts = LabVisibilityDemoSeed.Create()
            .Select(item => item.Content)
            .Where(content => content.State == LabVisibilityState.Draft)
            .Select(content => Laboratory.Rehydrate(content.LabId, content.CourseId, content.Title,
                LabStatus.Draft, content.AuthorId, createdAt))
            .Append(Laboratory.Rehydrate(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                LabVisibilityDemoSeed.DemoCourseId, "LAB-05: Pretraga i kontrola pristupa",
                LabStatus.Draft, LabVisibilityDemoSeed.TeacherId, createdAt));

        foreach (Laboratory draft in drafts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await repository.GetByIdAsync(draft.Id, cancellationToken) is null)
            {
                await repository.AddAsync(draft, cancellationToken);
            }
        }
    }
}
