using System.Text;

namespace Platform.Api.Contracts.Identity;

public record MfaEnrollResponse(string Uri)
{
    // US-22: the otpauth:// URI carries the secret; ToString() of a record would print it into any log.
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Uri = ***");
        return true;
    }
}
