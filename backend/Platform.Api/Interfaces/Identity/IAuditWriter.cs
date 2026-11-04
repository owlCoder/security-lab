using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface IAuditWriter
{
    void Write(AuditEvent e);
}
