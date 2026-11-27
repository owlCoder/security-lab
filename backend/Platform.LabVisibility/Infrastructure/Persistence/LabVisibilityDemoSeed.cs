using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// Izmišljeni demo podaci za lokalni razvoj i KT1 demo. Isti ID-jevi kao u frontend mock-u (Ferjuc).
public static class LabVisibilityDemoSeed
{
    public static readonly Guid DemoCourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid PublishedLabId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid DraftLabId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    // Prolazi kroz domensku validaciju kao i svaki drugi sadržaj.
    public static IReadOnlyList<(LabPublicContent Content, EvaluatorKey Key)> Create()
    {
        var published = LabPublicContent.Create(
            PublishedLabId,
            DemoCourseId,
            TeacherId,
            LabVisibilityState.Published,
            "Analiza sumnjivih prijava",
            "Prepoznaj napad pogađanja lozinke u dnevniku prijava.",
            [
                new PublicTask("t1", 1, "Posle koliko neuspešnih pokušaja je nalog student@example.com zaključan?", ["3", "5", "10"]),
                new PublicTask("t2", 2, "Koja mera najviše usporava napad pogađanja lozinke?", ["Ograničenje broja pokušaja", "Duža sesija", "Duža lozinka za administratora"])
            ]);

        var draft = LabPublicContent.Create(
            DraftLabId,
            DemoCourseId,
            TeacherId,
            LabVisibilityState.Draft,
            "Provera integriteta preuzetog fajla",
            "Uporedi heš preuzetog fajla sa objavljenom vrednošću.",
            [
                new PublicTask("t1", 1, "Koji algoritam je pogodan za proveru integriteta?", ["MD5", "SHA-256", "Base64"])
            ]);

        return
        [
            (published, EvaluatorKey.Create(published, [new TaskAnswerKey("t1", "5"), new TaskAnswerKey("t2", "Ograničenje broja pokušaja")])),
            (draft, EvaluatorKey.Create(draft, [new TaskAnswerKey("t1", "SHA-256")]))
        ];
    }

    // Poziva se samo u Development okruženju (Program.cs). Sa connection string-om primeni migracije
    // i upiše demo laboratorije u PostgreSQL; bez njega ih upiše u InMemory skladište.
    public static async Task SeedLabVisibilityDemoAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(LabVisibilityDemoSeed));
        var labs = Create();
        var keys = services.GetRequiredService<IEvaluatorKeyStore>();

        if (!LabVisibilityDatabase.IsConfigured(services))
        {
            var reader = services.GetRequiredService<InMemoryLabPublicContentReader>();
            foreach (var (content, key) in labs)
            {
                reader.Save(content);
                await keys.SaveAsync(key, cancellationToken);
            }

            logger.LogInformation("LabVisibility: nema ConnectionStrings:LabVisibility, demo laboratorije su u memoriji.");
            return;
        }

        try
        {
            var dbFactory = services.GetRequiredService<IDbContextFactory<LabVisibilityDbContext>>();
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                await db.Database.MigrateAsync(cancellationToken);

                foreach (var (content, _) in labs)
                {
                    if (!await db.PublicContents.AnyAsync(x => x.LabId == content.LabId, cancellationToken))
                        db.PublicContents.Add(LabVisibilityMapping.ToRow(content));
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            foreach (var (_, key) in labs)
                await keys.SaveAsync(key, cancellationToken);

            logger.LogInformation("LabVisibility: migracije primenjene, demo laboratorije upisane u PostgreSQL.");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Bez sadržaja ključa u poruci: loguje se samo razlog greške baze.
            logger.LogWarning("LabVisibility: baza nije spremna, demo podaci nisu upisani: {Message}", ex.Message);
        }
    }
}
