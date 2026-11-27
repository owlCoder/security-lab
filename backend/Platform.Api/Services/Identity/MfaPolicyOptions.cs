using Microsoft.Extensions.Options;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public static class MfaPolicyServiceCollectionExtensions
{
    /// <summary>US-19: roles that must use MFA, read from configuration and validated at startup.</summary>
    public static IServiceCollection AddMfaRolePolicy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MfaPolicyOptions>()
            .Bind(configuration.GetSection(MfaPolicyOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MfaPolicyOptions>, MfaPolicyOptionsValidator>();
        services.AddSingleton<IMfaPolicy, RoleMfaPolicy>();
        return services;
    }
}

public sealed class MfaPolicyOptions
{
    public const string Section = "Identity:Mfa";

    public List<string> RequiredForRoles { get; set; } = [];
}

public sealed class MfaPolicyOptionsValidator : IValidateOptions<MfaPolicyOptions>
{
    public ValidateOptionsResult Validate(string? name, MfaPolicyOptions options)
    {
        var known = Enum.GetNames<AccountRole>();

        var errors = options.RequiredForRoles
            .Where(role => !known.Contains(role, StringComparer.Ordinal))
            .Select(role =>
                $"{MfaPolicyOptions.Section}:RequiredForRoles sadrži nepoznatu ulogu '{role}'. " +
                $"Dozvoljene su: {string.Join(", ", known)}.")
            .ToList();

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
