using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IAccountRegistration
{
    ValueTask<RegisterAccountResult> RegisterAsync(
        RegisterAccountCommand command,
        CancellationToken cancellationToken = default);
}