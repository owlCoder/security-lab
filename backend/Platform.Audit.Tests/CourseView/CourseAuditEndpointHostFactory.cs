using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Audit.Tests.Authorization;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// The real Platform.Api host (Program.cs composition, Testing environment) with test-only
/// replacements for what other teams have not delivered yet:
/// <list type="bullet">
/// <item>session: the synthetic Testing session from the S-10 correlation tests,</item>
/// <item>subject: <see cref="HeaderTestSubjectProvider"/> instead of a session-based Access subject,</item>
/// <item>memberships: <see cref="StubCourseAuditMemberships"/> instead of ACC-02/ACC-07.</item>
/// </list>
/// Optionally the S-07 query service or the trusted writer is replaced to force a result.
/// Nothing here is registered by Platform.Api itself.
/// </summary>
internal sealed class CourseAuditEndpointHostFactory : WebApplicationFactory<Program>
{
    /// <summary>A closed local port without a password: every database call fails fast.</summary>
    internal const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=audit_unreachable;Username=audit_unreachable;Timeout=1;Pooling=false";

    public StubCourseAuditMemberships Memberships { get; } = new();
    public CourseViewCapturingLoggerProvider Logs { get; } = new();
    public string ConnectionString { get; init; } = UnreachableDatabase;
    public ICourseAuditQueryService? Query { get; init; }
    public ITrustedAuditWriter? Writer { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(Logs);
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(ConnectionString));

            services.RemoveAll<ISessionManager>();
            services.AddSingleton<ISessionManager, CorrelationTestSessionManager>();
            services.RemoveAll<IAccessSubjectProvider>();
            services.AddScoped<IAccessSubjectProvider, HeaderTestSubjectProvider>();
            services.RemoveAll<IMembershipReader>();
            services.AddSingleton<IMembershipReader>(Memberships);

            if (Query is not null)
            {
                services.RemoveAll<ICourseAuditQueryService>();
                services.AddSingleton(Query);
            }

            if (Writer is not null)
            {
                services.RemoveAll<ITrustedAuditWriter>();
                services.AddSingleton(Writer);
            }
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={CorrelationTestSessionManager.Token}");
    }
}

/// <summary>S-07 query service with a scripted answer; records each query it receives.</summary>
internal sealed class ScriptedCourseAuditQueryService : ICourseAuditQueryService
{
    private readonly ConcurrentQueue<CourseAuditQuery> queries = new();
    public IReadOnlyList<CourseAuditQuery> Queries => queries.ToArray();
    public Func<CourseAuditQuery, CourseAuditQueryResult> Handler { get; set; } = query => Page(query.CourseId);

    public Task<CourseAuditQueryResult> QueryAsync(CourseAuditQuery query, CancellationToken cancellationToken = default)
    {
        queries.Enqueue(query);
        return Task.FromResult(Handler(query));
    }

    public static readonly Guid EventId = Guid.Parse("e0e0e0e0-0000-4000-8000-000000000001");
    public static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset To = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    public static CourseAuditQueryResult Page(Guid courseId) => CourseAuditQueryResult.Ok(new CourseAuditPage(
        courseId, From, To, 1, 50, false,
        [
            new CourseAuditEventDto(EventId, To.AddHours(-1), AuditEventTypes.AccessDenied, AuditOutcome.Denied,
                AuditActorKind.Anonymous, null, AuditResourceTypes.Course, courseId.ToString("D"), "corr-seed-0001")
        ]));
}

internal sealed class CourseViewCapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, EventId EventId, string Text)> entries = new();
    public IReadOnlyList<(string Category, EventId EventId, string Text)> Entries => entries.ToArray();
    public string AllText => string.Join("\n", entries.Select(entry => entry.Text));

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);
    public void Dispose() { }

    private sealed class Logger(string category, ConcurrentQueue<(string, EventId, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, eventId, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}
