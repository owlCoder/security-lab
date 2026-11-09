using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Contracts;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 / ASN-06 TIM 15 (celina 5)
// Pravi API u memoriji (Development), ceo HTTP pipeline kao u aplikaciji:
// CorrelationMiddleware -> AccessControlMiddleware -> SessionCheckMiddleware -> AssignmentsController
// -> use-case (član 2/3) -> naš audit adapter. Zamenjeni su samo spoljni izvori: sesija (kolačić
// SecurityLab.Session=<userId>), status naloga, portovi članova 1 i 4 i skladište Audit modula.
internal sealed class AssignmentsHostFactory(
    bool useFakePorts = true,
    IAssignmentAuditWriter? assignmentAuditOverride = null) : WebApplicationFactory<Program>
{
    public const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    public const string Teacher = "11111111-1111-1111-1111-111111111111";
    public const string Student = "22222222-2222-2222-2222-222222222222";
    public const string Outsider = "33333333-3333-3333-3333-333333333333";
    public const string BlockedStudent = "55555555-5555-5555-5555-555555555555";

    private static readonly HashSet<string> KnownUsers = [Teacher, Student, Outsider, BlockedStudent];

    public InMemoryAssignmentRepository Assignments { get; } = new();

    public CountingAttemptStore Attempts { get; } = new();

    public AllowListStartAuthorization StartAuthorization { get; } = new();

    public AllowListDeadlineAuthorization DeadlineAuthorization { get; } = new();

    public AllowListEnrollment Enrollment { get; } = new();

    public InMemoryDeadlineExtensionStore DeadlineStore { get; } = new();

    public RecordingTrustedAuditWriter Audit { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, TestSessionManager>();

            services.RemoveAll<IAccountStatusReader>();
            services.AddSingleton<IAccountStatusReader, TestAccounts>();

            // Audit modul (tim 12) bez baze; ponaša se kao pravi katalog.
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddSingleton<ITrustedAuditWriter>(Audit);

            // Za testove pada audita: writer koji baca izuzetak umesto da vrati false.
            if (assignmentAuditOverride is not null)
            {
                services.RemoveAll<IAssignmentAuditWriter>();
                services.AddSingleton(assignmentAuditOverride);
            }

            if (!useFakePorts)
            {
                return; // Podrazumevana kompozicija: fail-closed portovi iz AddAssignmentsApi().
            }

            services.RemoveAll<IAssignmentRepository>();
            services.AddSingleton<IAssignmentRepository>(Assignments);
            services.RemoveAll<IAttemptStore>();
            services.AddSingleton<IAttemptStore>(Attempts);
            services.RemoveAll<IStartAttemptAuthorization>();
            services.AddSingleton<IStartAttemptAuthorization>(StartAuthorization);
            services.RemoveAll<IDeadlineExtensionAuthorization>();
            services.AddSingleton<IDeadlineExtensionAuthorization>(DeadlineAuthorization);
            services.RemoveAll<ICourseEnrollmentCheck>();
            services.AddSingleton<ICourseEnrollmentCheck>(Enrollment);
            services.RemoveAll<IDeadlineExtensionStore>();
            services.AddSingleton<IDeadlineExtensionStore>(DeadlineStore);
        });
    }

    public static HttpRequestMessage StartAttempt(Guid assignmentId, string? user, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/assignments/{assignmentId:D}/attempts");
        AddSession(request, user);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    public static HttpRequestMessage ExtendDeadline(Guid assignmentId, string studentId, string? user, string json)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/assignments/{assignmentId:D}/students/{studentId}/deadline-extensions")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        AddSession(request, user);
        return request;
    }

    private static void AddSession(HttpRequestMessage request, string? user)
    {
        if (user is not null)
        {
            request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        }
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

    private sealed class TestAccounts : IAccountStatusReader
    {
        public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            var id = accountId.ToString("D");
            if (!KnownUsers.Contains(id))
            {
                return Task.FromResult<AccountSnapshot?>(null);
            }

            var status = id == BlockedStudent ? AccountStatus.Blocked : AccountStatus.Active;
            return Task.FromResult<AccountSnapshot?>(new AccountSnapshot(accountId, status));
        }
    }
}
