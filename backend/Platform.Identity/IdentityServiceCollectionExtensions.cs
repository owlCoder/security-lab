using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;

namespace Platform.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddDbContext<AccountDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<IConfiguration>().GetConnectionString("Identity"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            })
            .AddEntityFrameworkStores<AccountDbContext>();
            
            services.AddScoped<IAccountRegistration, AccountRegistrationService>();

        return services;
    }
}