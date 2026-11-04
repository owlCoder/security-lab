using Platform.Identity.Entities;

namespace Platform.Api.Interfaces.Identity;

public interface IAccountRepository
{
    UserAccount? Find(Guid id);
    void Save(UserAccount account);
}