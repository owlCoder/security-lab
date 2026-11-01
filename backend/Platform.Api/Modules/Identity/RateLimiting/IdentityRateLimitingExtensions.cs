using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Platform.Api.Modules.Identity.RateLimiting;

/// <summary>
/// IDN-05: ograničenje broja pokušaja prijave i reseta lozinke po IP adresi klijenta.
/// </summary>
public static class IdentityRateLimitingExtensions
{
    private const string LogCategory = "Platform.Api.Modules.Identity.RateLimiting";

    public static IServiceCollection AddIdentityRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentityRateLimitOptions>()
            .Bind(configuration.GetSection(IdentityRateLimitOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityRateLimitOptions>, IdentityRateLimitOptionsValidator>();

        // X-Forwarded-For se prihvata ISKLJUČIVO od eksplicitno navedenih proxy-ja.
        // Bez toga bi napadač slao lažnu adresu u svakom zahtevu i zaobišao limiter.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<IdentityRateLimitOptions>>((forwarded, rateLimit) =>
            {
                forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                forwarded.ForwardLimit = 1; // uzima se samo adresa koju je dopisao naš proxy
                forwarded.KnownProxies.Clear();
                forwarded.KnownIPNetworks.Clear(); // podrazumevano veruje loopback-u; ovde ništa nije implicitno
                foreach (var proxy in rateLimit.Value.KnownProxies)
                    if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
                foreach (var network in rateLimit.Value.KnownNetworks)
                    if (System.Net.IPNetwork.TryParse(network, out var parsed)) forwarded.KnownIPNetworks.Add(parsed);

                // Pažnja: kada su obe liste prazne, ForwardedHeadersMiddleware NE proverava izvor
                // i prihvata header od bilo koga. Zato bez podešenog proxy-ja potpuno isključujemo obradu.
                if (forwarded.KnownProxies.Count == 0 && forwarded.KnownIPNetworks.Count == 0)
                    forwarded.ForwardedHeaders = ForwardedHeaders.None;
            });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicies.Login, http => Partition(http, o => o.Login));
            limiter.AddPolicy(RateLimitPolicies.PasswordReset, http => Partition(http, o => o.PasswordReset));
            limiter.OnRejected = OnRejected;
        });

        return services;
    }

    /// <summary>Mora se pozvati pre autentifikacije i mapiranja endpointa.</summary>
    public static WebApplication UseIdentityRateLimiting(this WebApplication app)
    {
        app.UseForwardedHeaders(); // prvo: RemoteIpAddress postaje stvarna adresa klijenta
        app.UseRouting();          // limiter mora da vidi endpoint da bi znao koja politika važi
        app.UseRateLimiter();
        return app;
    }

    /// <summary>
    /// Ključ particije. IPv6 se svodi na /64 jer jedan korisnik obično dobija ceo /64 blok
    /// i mogao bi da menja adrese unutar njega.
    /// </summary>
    public static string ClientPartitionKey(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    private static RateLimitPartition<string> Partition(HttpContext http, Func<IdentityRateLimitOptions, WindowLimit> select)
    {
        var limit = select(http.RequestServices.GetRequiredService<IOptions<IdentityRateLimitOptions>>().Value);
        // Sliding window: nema "duple ture" na granici prozora kao kod fixed window.
        return RateLimitPartition.GetSlidingWindowLimiter(
            ClientPartitionKey(http.Connection.RemoteIpAddress),
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = TimeSpan.FromSeconds(limit.WindowSeconds),
                SegmentsPerWindow = SegmentsPerWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private const int SegmentsPerWindow = 6;

    private static async ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        // Isti identifikator koji koriste ostali delovi identity modula (AUD-04, OPS-08).
        var correlation = http.TraceIdentifier;
        http.Response.Headers["X-Correlation-ID"] = correlation;
        http.Response.Headers.CacheControl = "no-store";

        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
        http.Response.Headers.RetryAfter = RetryAfterSeconds(context.Lease, http, policy)
            .ToString(CultureInfo.InvariantCulture);

        // Bez emaila, lozinke ili tela zahteva u logu (AUD-03).
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LogCategory).LogWarning(
            "Rate limit exceeded. Policy={Policy} Client={Client} CorrelationId={CorrelationId}",
            policy, ClientPartitionKey(http.Connection.RemoteIpAddress), correlation);

        // Isti odgovor bez obzira na nalog, da se ne bi otkrilo da li postoji (IDN-04, OPS-07).
        await http.Response.WriteAsJsonAsync(new
        {
            code = "rate_limited",
            message = "Previse pokusaja. Pokusajte ponovo kasnije."
        }, cancellationToken);
    }

    /// <summary>
    /// Sliding window ne vraća uvek RetryAfter u metapodacima. Tada se vraća trajanje celog prozora:
    /// to je jedina vrednost za koju je sigurno da posle nje zahtev prolazi. Kraće vreme (npr. jedan
    /// segment) moglo bi da bude premalo ako su svi zahtevi stigli u poslednjem segmentu.
    /// </summary>
    private static int RetryAfterSeconds(RateLimitLease lease, HttpContext http, string policy)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));

        var options = http.RequestServices.GetRequiredService<IOptions<IdentityRateLimitOptions>>().Value;
        var window = policy == RateLimitPolicies.PasswordReset ? options.PasswordReset : options.Login;
        return Math.Max(1, window.WindowSeconds);
    }
}
