using Platform.Access.Domain.Models;

namespace Platform.Access.Infrastructure.Grants;

public interface IElevatedGrantValidator
{
    bool IsValid(
        ElevatedGrant grant,
        Guid holderId,
        string action,
        ResourceDescriptor resource,
        DateTimeOffset now);
}
