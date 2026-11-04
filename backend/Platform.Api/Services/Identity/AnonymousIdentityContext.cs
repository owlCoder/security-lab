using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Api.Services.Identity;

/// Izvan Developmenta nema pozivaoca, pa je svaki zahtev 401.
public sealed class AnonymousIdentityContext : IIdentityContext
{
    public CallerIdentity? Current => null;
}
