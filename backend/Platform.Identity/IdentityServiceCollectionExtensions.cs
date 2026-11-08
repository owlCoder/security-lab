using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Npgsql;

namespace Platform.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.TryAddScoped<NpgsqlConnection>(sp =>
            new NpgsqlConnection(
                sp.GetRequiredService<IConfiguration>().GetConnectionString(IdentityModule.ConnectionStringName)
                ?? string.Empty));

        services.AddDbContext<AccountDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<NpgsqlConnection>(),
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