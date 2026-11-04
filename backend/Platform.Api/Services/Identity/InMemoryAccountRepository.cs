using System.Collections.Concurrent;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Entities;

namespace Platform.Api.Services.Identity;

// TODO(IDN-03 / ADM-02): PRIVREMENO. Podaci su samo u memoriji i nestaju kad se aplikacija ugasi,
// pa kriterijum "sistem čuva novi status u bazi" nije do kraja ispunjen.
// Trajno čuvanje (PostgreSQL + migracija) radi [ime osobe / task]. Kad bude gotovo, dovoljno je
// da se napravi nova implementacija IAccountRepository i zameni registracija u Program.cs.
// Use-case (ChangeAccountStatus) i testovi se ne menjaju.
public sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly ConcurrentDictionary<Guid, UserAccount> _items = new();
    public UserAccount? Find(Guid id) => _items.GetValueOrDefault(id);
    public void Save(UserAccount account) => _items[account.Id] = account;
}
