using Platform.Identity.Domain;

namespace Platform.Api.Services.Identity;

public interface IAccountRepository
{
    UserAccount? Find(Guid id);
    void Save(UserAccount account);
}