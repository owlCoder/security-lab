using Platform.Access.Domain.Enums;
namespace Platform.Membership.Contracts;
/// <summary>
/// Status naloga koji se upisuje (aktivan, blokiran, arhiviran). Nalog pripada Identity timovima
/// (IDN-01, IDN-03); Platform.Api daje adapter nad njihovim podacima.
/// Vraća null kada nalog ne postoji.
/// </summary>
public interface IAccountStatusReader
{
    ValueTask<AccountState?> GetStatusAsync(Guid accountId, CancellationToken cancellationToken = default);
}