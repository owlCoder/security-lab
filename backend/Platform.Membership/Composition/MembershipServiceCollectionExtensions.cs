using Platform.Access.Contracts;
using Platform.Membership.Infrastructure.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.Membership.Application;
using Platform.Membership.Contracts;
using Platform.Membership.Infrastructure;
using Platform.Membership.Infrastructure.Accounts;
using Platform.Membership.Infrastructure.Courses;
using Platform.Membership.Infrastructure.Persistence;
namespace Platform.Membership.Composition;

public static class MembershipServiceCollectionExtensions
{
    /// <summary>
    /// Uključuje modul za članstvo u kursu (ACC-02). Poziva ga AddMembership() iz Platform.Api,
    /// pre AddAccessControl().
    /// </summary>
    public static IServiceCollection AddMembershipModule(
    this IServiceCollection services,
    IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        // Fabrika umesto direktnog DbContext-a: politike pristupa su singleton,
        // pa svaka operacija otvara kratak, sopstveni kontekst.
        services.AddDbContextFactory<MembershipDbContext>((sp, options) =>
        options.UseNpgsql(
        sp.GetRequiredService<IConfiguration>().GetConnectionString(MembershipConnection.Name),
        npgsql => npgsql.MigrationsHistoryTable(
        "__EFMigrationsHistory", MembershipDbContext.Schema)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMembershipStore, EfMembershipStore>();
        // Platform.Api registruje pravi adapter nad nalozima; dok ga nema, ništa ne prolazi.
        services.TryAddSingleton<IAccountStatusReader, UnavailableAccountStatusReader>();
        if (environment.IsDevelopment())
        {
            services.AddSingleton<ICourseReader, DevelopmentCourseReader>();
        }
        else
        {
            services.AddSingleton<ICourseReader, UnavailableCourseReader>();
        }
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddSingleton<IMembershipReader, StoreMembershipReader>();
        return services;
    }
}