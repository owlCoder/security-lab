using Microsoft.EntityFrameworkCore;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;

namespace Platform.LabVisibility.Infrastructure.Persistence;

// AUT-07 (KT2): uslovna promena stanja u lab_public_content.
// Jedan UPDATE ... WHERE lab_id = @id AND state = @expected: ako je neko u međuvremenu
// promenio stanje, nijedan red se ne menja i rezultat je false (model pretnji T27).
public sealed class EfLabPublicationStateStore(IDbContextFactory<LabVisibilityDbContext> dbFactory) : ILabPublicationStateStore
{
    public async ValueTask<bool> TryChangeStateAsync(
        Guid labId,
        LabVisibilityState expected,
        LabVisibilityState next,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var expectedValue = expected.ToString();
        var nextValue = next.ToString();

        var changed = await db.PublicContents
            .Where(x => x.LabId == labId && x.State == expectedValue)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, nextValue), cancellationToken);

        return changed == 1;
    }
}
