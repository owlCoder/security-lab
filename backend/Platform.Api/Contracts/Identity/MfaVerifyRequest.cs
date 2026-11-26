using System.Text;

namespace Platform.Api.Contracts.Identity;

public sealed record MfaVerifyRequest(string Code)
{
    // US-22: MVC logs action arguments with ToString() at Trace level, and a record prints every member.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Code = ***");
        return true;
    }
}
