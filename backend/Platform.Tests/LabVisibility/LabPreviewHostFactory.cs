using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-07: pravi API u memoriji (Development), ceo HTTP pipeline kao u aplikaciji:
// SessionCheckMiddleware -> SessionIdentityContext -> LabCaller -> PreviewStudentView.
// Zamenjeni su samo spoljni izvori: sesija (kolačić SecurityLab.Session=<userId> važi za
// test korisnike), status naloga (svi aktivni) i skladište (uvek InMemory).
internal sealed class LabPreviewHostFactory : WebApplicationFactory<Program>
{
    public const string Teacher = "11111111-1111-1111-1111-111111111111";
    public const string Student = "22222222-2222-2222-2222-222222222222";
    public const string Outsider = "33333333-3333-3333-3333-333333333333";
    public const string OtherCourseTeacher = "44444444-4444-4444-4444-444444444444";

    private static readonly HashSet<string> KnownUsers = [Teacher, Student, Outsider, OtherCourseTeacher];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, TestSessionManager>();

            services.RemoveAll<IAccountStatusReader>();
            services.AddSingleton<IAccountStatusReader, ActiveTestAccounts>();

            // Uvek InMemory, i kad lokalno postoji ConnectionStrings:LabVisibility.
            services.RemoveAll<ILabPublicContentReader>();
            services.AddSingleton<ILabPublicContentReader>(
                sp => sp.GetRequiredService<InMemoryLabPublicContentReader>());
            services.RemoveAll<IEvaluatorKeyStore>();
            services.AddSingleton<IEvaluatorKeyStore>(
                sp => sp.GetRequiredService<InMemoryEvaluatorKeyStore>());
        });
    }

    // Prijavljen korisnik = sesijski kolačić; null = zahtev bez sesije.
    public static HttpRequestMessage PreviewRequest(Guid labId, string? user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{labId:D}/preview");
        if (user is not null)
        {
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        }

        return request;
    }

    private sealed class TestSessionManager : ISessionManager
    {
        public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(KnownUsers.Contains(token)
                ? new ValidateSession(Guid.NewGuid(), Guid.Parse(token), AuthenticationLevel.Active)
                : null);

        public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ActiveTestAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(KnownUsers.Contains(accountId.ToString("D"))
                ? new AccountSnapshot(accountId, AccountStatus.Active)
                : null);
    }
}
