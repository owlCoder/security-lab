using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Interfaces.Identity;
using Platform.Authoring.Composition;

namespace Platform.Authoring.Tests.Support;

/// <summary>Builds the Access and Authoring modules the way Program.cs does, with strict DI validation.</summary>
internal static class TestHost
{
    public static readonly Guid Author = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static SubjectRef Active(Guid id) => new(id.ToString(), AccountState.Active);

    public static IConfiguration Config(params Guid[] authors) =>
        RawConfig(authors.Select(a => a.ToString()).ToArray());

    public static IConfiguration RawConfig(params string[] authors) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(authors.Select((value, i) =>
                new KeyValuePair<string, string?>($"Authoring:AuthorAccountIds:{i}", value)))
            .Build();

    public static ServiceProvider Build(params Guid[] authors)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Config(authors));
        services.AddAccessControl();
        services.AddAuthoring();

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        provider.RegisterAuthoringAccess();
        return provider;
    }
}

/// <summary>Returns a fixed subject, like the server-side session lookup would. Null means "not signed in".</summary>
internal sealed class FixedSubjectResolver(SubjectRef? subject) : IRequestSubjectResolver
{
    public SubjectRef? Resolve(HttpContext context) => subject;
}
