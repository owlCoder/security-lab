using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Team21.Acc.Infrastructure.Common;

/// <summary>
/// Npgsql only takes Utc DateTimes for timestamptz columns.
/// </summary>
public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        // C# -> DB (write): Local carries the real instant so it is converted. Only Unspecified is stamped
        // because nothing else is known about it.
        v => v.Kind == DateTimeKind.Utc ? v
            : v.Kind == DateTimeKind.Local ? v.ToUniversalTime()
            : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        // DB -> C# (read): Npgsql already returns Utc; stamp defensively so it's never Unspecified
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    { }
}
