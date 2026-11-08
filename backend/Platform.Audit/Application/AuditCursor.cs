using System.Globalization;

namespace Platform.Audit.Application;

/// <summary>
/// Keyset position (occurred_at, id) of the last event of a page. Not a secret and not
/// trusted: it is parsed strictly and only narrows a query that is already limited to one course.
/// Format: "{utcTicks}_{idN}".
/// </summary>
internal readonly record struct AuditCursor(DateTimeOffset OccurredAt, Guid Id)
{
    public string Format() =>
        string.Create(CultureInfo.InvariantCulture, $"{OccurredAt.UtcTicks}_{Id:N}");

    public static bool TryParse(string? value, out AuditCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;

        var separator = value.IndexOf('_');
        if (separator <= 0 || separator == value.Length - 1)
            return false;

        var ticksPart = value.AsSpan(0, separator);
        var idPart = value.AsSpan(separator + 1);

        if (!long.TryParse(ticksPart, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTime.MinValue.Ticks
            || ticks > DateTime.MaxValue.Ticks
            || idPart.Length != 32
            || !Guid.TryParseExact(idPart, "N", out var id)
            || id == Guid.Empty)
            return false;

        cursor = new AuditCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
