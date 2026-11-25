using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Modules.Administration;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;

namespace Platform.Coursework.Demo;

internal static class LiveScenario
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Path(Guid course) => "/api/course-administration/" + course.ToString("D");

    public static async Task RunAsync(DemoFixture f, LiveApiProcess api, LiveReport report, CancellationToken ct)
    {
        report.Stage = "verify-ephemeral-tls-pin";
        var checkedPin = false;
        using (var badPin = api.CreateClient(wrongPin: true, observeCertificateCheck: accepted => checkedPin = !accepted))
        {
            var rejected = false;
            try { using var response = await badPin.GetAsync("/api/health", ct); }
            catch (HttpRequestException) { rejected = true; }
            report.Check("A different TLS pin is rejected", rejected && checkedPin, "tls");
        }
        using (var anonymous = new LiveSession(api, report))
        {
            await anonymous.SendAsync("Platform health remains available", HttpMethod.Get, "/api/health", null, [200], ct);
            await anonymous.SendAsync("Anonymous administrative read", HttpMethod.Get, Path(f.CourseA), null, [401], ct);
            await anonymous.SendAsync("Anonymous settings write", HttpMethod.Put, Path(f.CourseA) + "/settings", new RenameCourseRequest("no", 1), [401], ct);
            await anonymous.SendAsync("Incorrect password is rejected", HttpMethod.Post, "/api/auth/login",
                new { Email = DemoFixture.Email(f.RootA), Password = Guid.NewGuid().ToString("N") }, [401], ct);
        }

        using var rootA = new LiveSession(api, report);
        using var rootB = new LiveSession(api, report);
        using var delegated = new LiveSession(api, report);
        using var viewer = new LiveSession(api, report);
        using var teacher = new LiveSession(api, report);
        await rootA.LoginAsync("Course A administrator", f.RootA, f.Password, ct);
        await rootB.LoginAsync("Course B administrator", f.RootB, f.Password, ct);
        await delegated.LoginAsync("Delegate signs in before receiving a grant", f.Delegate, f.Password, ct);
        await viewer.LoginAsync("View-only account signs in", f.Viewer, f.Password, ct);
        await teacher.LoginAsync("Teacher signs in without administrative authority", f.Teacher, f.Password, ct);
        var originalCookie = delegated.Cookie;

        await rootA.SendAsync("Root reads own course administration", HttpMethod.Get, Path(f.CourseA), null, [200], ct);
        var foreign = await rootA.SendAsync("Root cannot administer another course", HttpMethod.Get, Path(f.CourseB), null, [404], ct);
        var missing = await rootA.SendAsync("Missing course is unavailable", HttpMethod.Get, Path(Guid.NewGuid()), null, [404], ct);
        report.Check("Foreign and missing public responses are identical", foreign.Body == missing.Body);
        report.Check("Administrative responses prohibit caching", foreign.NoStore && missing.NoStore);
        await teacher.SendAsync("Teaching role does not grant course administration", HttpMethod.Get, Path(f.CourseA), null, [404], ct);
        await delegated.SendAsync("Existing session has no authority before delegation", HttpMethod.Get, Path(f.CourseA), null, [404], ct);
        await rootA.SendAsync("Delegation cannot grant ManageDelegations", HttpMethod.Post, Path(f.CourseA) + "/delegations",
            new DelegateRequest(f.Delegate, ["ManageDelegations"]), [400], ct);

        var grantReply = await rootA.SendAsync("Root grants limited administration", HttpMethod.Post, Path(f.CourseA) + "/delegations",
            new DelegateRequest(f.Delegate, ["ViewAdministration", "ManageSettings"]), [200], ct);
        var grant = grantReply.Parse<GrantView>();
        report.Check("Grant response is bound to the requested course/account", grant.Id != Guid.Empty && grant.CourseId == f.CourseA && grant.GranteeId == f.Delegate);
        await rootA.SendAsync("Root grants view-only authority", HttpMethod.Post, Path(f.CourseA) + "/delegations",
            new DelegateRequest(f.Viewer, ["ViewAdministration"]), [200], ct);
        await delegated.SendAsync("Original pre-grant cookie now reads administration", HttpMethod.Get, Path(f.CourseA), null, [200], ct);
        await viewer.SendAsync("View-only delegate reads administration", HttpMethod.Get, Path(f.CourseA), null, [200], ct);
        await viewer.SendAsync("View-only delegate cannot change settings", HttpMethod.Put, Path(f.CourseA) + "/settings", new RenameCourseRequest("no", 1), [404], ct);
        await delegated.SendAsync("Delegate cannot redelegate", HttpMethod.Post, Path(f.CourseA) + "/delegations",
            new DelegateRequest(f.Teacher, ["ViewAdministration"]), [404], ct);
        await rootB.SendAsync("Other-course root cannot revoke this grant", HttpMethod.Post, Path(f.CourseB) + $"/delegations/{grant.Id:D}/revoke",
            new RevokeRequest(1), [404], ct);
        await delegated.SendAsync("Delegate updates the permitted administrative label", HttpMethod.Put, Path(f.CourseA) + "/settings",
            new RenameCourseRequest("Persisted live administrative label", 1), [200], ct);
        await delegated.SendAsync("Stale version is rejected", HttpMethod.Put, Path(f.CourseA) + "/settings", new RenameCourseRequest("stale", 1), [409], ct);
        await using (var catalog = f.Catalog())
            report.Check("Administrative update does not rename the shared catalog course",
                (await catalog.Courses.AsNoTracking().SingleAsync(c => c.Id == f.CourseA, ct)).Name == "Live catalog course A");
        await rootA.SendAsync("Blank administrative input is rejected", HttpMethod.Put, Path(f.CourseA) + "/settings", new RenameCourseRequest("", 2), [400], ct);
        await rootA.SendAsync("Write without the required header is rejected", HttpMethod.Put, Path(f.CourseA) + "/settings",
            new RenameCourseRequest("no", 2), [400], ct, includeWriteHeader: false);
        await rootA.SendAsync("Foreign Origin is rejected", HttpMethod.Put, Path(f.CourseA) + "/settings",
            new RenameCourseRequest("no", 2), [400], ct, origin: "https://outside.example.invalid");
        await rootA.SendAsync("Course root does not gain global account registration", HttpMethod.Post, "/api/admin/accounts",
            new { Email = "not-created@example.invalid", Password = Guid.NewGuid().ToString("N") }, [401, 403], ct);
        await using (var identity = f.Identity())
            report.Check("Denied global registration created no account", !await identity.Accounts.AnyAsync(a => a.Email == "not-created@example.invalid", ct));

        report.Stage = "restart-real-api-with-persisted-state";
        foreach (var session in new[] { rootA, rootB, delegated, viewer, teacher }) session.Disconnect();
        await api.StopAsync();
        await api.StartAsync(ct);
        report.ApiRestarts++;
        foreach (var session in new[] { rootA, rootB, delegated, viewer, teacher }) session.Reconnect(api);
        var persisted = await delegated.SendAsync("Same cookie reads after a real API restart", HttpMethod.Get, Path(f.CourseA), null, [200], ct);
        var view = persisted.Parse<AdministrationView>();
        report.Check("PostgreSQL preserved the label and version across restart", view.DisplayName == "Persisted live administrative label" && view.Version == 2);
        await using (var identity = f.Identity())
            report.Check("Delegate has one session; no restart/revocation sign-in occurred", await identity.Sessions.CountAsync(s => s.UserId == f.Delegate, ct) == 1);

        await rootA.SendAsync("Root revokes delegated authority", HttpMethod.Post, Path(f.CourseA) + $"/delegations/{grant.Id:D}/revoke", new RevokeRequest(1), [200], ct);
        report.OriginalDelegateCookieReused = originalCookie is not null && delegated.Cookie == originalCookie;
        report.Check("The exact previously issued delegate cookie is retained", report.OriginalDelegateCookieReused);
        await delegated.SendAsync("Revoked delegate cannot read with the original cookie", HttpMethod.Get, Path(f.CourseA), null, [404], ct);
        await delegated.SendAsync("Revoked delegate cannot write with the original cookie", HttpMethod.Put, Path(f.CourseA) + "/settings",
            new RenameCourseRequest("no", 2), [404], ct);

        // Explicit fixture changes through the real provider model; no production status-changing endpoint is introduced.
        await using (var identity = f.Identity())
            await identity.Accounts.Where(a => a.Id == f.Viewer).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AccountStatus.Blocked), ct);
        await viewer.SendAsync("Blocked account loses access with an existing cookie", HttpMethod.Get, Path(f.CourseA), null, [401], ct);
        await using (var catalog = f.Catalog())
        { var course = await catalog.Courses.SingleAsync(c => c.Id == f.CourseB, ct); course.Archive(f.RootB); await catalog.SaveChangesAsync(ct); }
        await rootB.SendAsync("Archived catalog course denies its existing administrator", HttpMethod.Get, Path(f.CourseB), null, [404], ct);

        report.Stage = "relay-actual-producer-events";
        await f.OperatorAsync(["relay-once"], ct);
        var acknowledgements = await f.CountAsync("SELECT count(*) FROM coursework_admin.outbox_deliveries", ct);
        report.Check("Central Audit acknowledges supported producer events", acknowledgements > 0);
        report.Check("Both grants and the revocation reached central Audit per capability",
            await f.CountAsync("SELECT count(*) FROM audit.audit_events WHERE event_type IN ('course.delegation.granted','course.delegation.revoked')", ct) == 5);
        await f.OperatorAsync(["relay-once"], ct);
        report.Check("Relay retry does not duplicate central delegation records",
            await f.CountAsync("SELECT count(*) FROM audit.audit_events WHERE event_type IN ('course.delegation.granted','course.delegation.revoked')", ct) == 5);
        report.Check("Unmapped producer events are retained without false receipts", await f.CountAsync("""
            SELECT count(*) FROM coursework_admin.outbox e LEFT JOIN coursework_admin.outbox_deliveries d USING(event_id)
            WHERE e.outcome='ALLOW' AND e.action IN ('course.settings.update','course.root.bootstrap') AND d.event_id IS NULL
            """, ct) == 3);
        await rootA.SendAsync("Real logout completes", HttpMethod.Post, "/api/auth/logout", null, [204], ct);
        await rootA.SendAsync("Logged-out cookie cannot be replayed", HttpMethod.Get, Path(f.CourseA), null, [401], ct);
        report.Stage = "complete";
    }

    private sealed class LiveSession : IDisposable
    {
        private HttpClient? client;
        private readonly LiveReport report;
        public string? Cookie { get; private set; }
        public LiveSession(LiveApiProcess api, LiveReport report) { client = api.CreateClient(); this.report = report; }
        public void Disconnect() { client?.Dispose(); client = null; }
        public void Reconnect(LiveApiProcess api) => client = api.CreateClient();
        public async Task LoginAsync(string label, Guid account, string password, CancellationToken ct)
        {
            var reply = await SendAsync(label + " login", HttpMethod.Post, "/api/auth/login",
                new { Email = DemoFixture.Email(account), Password = password }, [200], ct);
            var issued = reply.IssuedCookies.Single(v => v.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal));
            var lower = issued.ToLowerInvariant();
            report.Check(label + " cookie is Secure/HttpOnly/SameSite=Strict", lower.Contains("secure") && lower.Contains("httponly") && lower.Contains("samesite=strict"));
            Cookie = issued.Split(';')[0];
            report.Check(label + " token is absent from the response body", Cookie.Length > SessionCookie.Name.Length + 21 && !reply.Body.Contains(Cookie[(SessionCookie.Name.Length + 1)..], StringComparison.Ordinal));
        }
        public async Task<Reply> SendAsync(string name, HttpMethod method, string path, object? body, int[] expected,
            CancellationToken ct, bool includeWriteHeader = true, string? origin = null)
        {
            report.Stage = name;
            using var request = new HttpRequestMessage(method, path);
            if (Cookie is not null) request.Headers.Add("Cookie", Cookie);
            if (includeWriteHeader) request.Headers.Add("X-Coursework-Request", "1");
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client!.SendAsync(request, ct);
            var status = (int)response.StatusCode;
            report.Check(name, expected.Contains(status), "http", expected, status);
            var content = await response.Content.ReadAsStringAsync(ct);
            return new(content, response.Headers.CacheControl?.NoStore == true,
                response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToArray() : []);
        }
        public void Dispose() => Disconnect();
    }
    private sealed record Reply(string Body, bool NoStore, string[] IssuedCookies)
    {
        public T Parse<T>() where T : class => JsonSerializer.Deserialize<T>(Body, Json) ?? throw new InvalidOperationException("Missing expected response DTO.");
    }
}
