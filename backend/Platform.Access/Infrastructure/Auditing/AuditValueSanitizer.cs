using System.Globalization;
using System.Text;

namespace Platform.Access.Infrastructure.Auditing;

internal static class AuditValueSanitizer
{
    public static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                if (!IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(ch, value[i + 1]))))
                {
                    if (builder.Length + 2 > maxLength)
                    {
                        break;
                    }

                    builder.Append(ch).Append(value[i + 1]);
                }

                i++;
                continue;
            }

            if (char.IsSurrogate(ch) || char.IsControl(ch) || IsUnsafe(CharUnicodeInfo.GetUnicodeCategory(ch)))
            {
                continue;
            }

            if (builder.Length == maxLength)
            {
                break;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static bool IsUnsafe(UnicodeCategory category) =>
        category is
            UnicodeCategory.Control or
            UnicodeCategory.LineSeparator or
            UnicodeCategory.ParagraphSeparator or
            UnicodeCategory.Format;
}
