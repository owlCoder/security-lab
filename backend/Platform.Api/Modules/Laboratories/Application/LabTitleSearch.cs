using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Platform.Api.Modules.Laboratories.Domain;

namespace Platform.Api.Modules.Laboratories.Application;

public static class LabTitleSearch
{
    public const int MaxQueryLength = Laboratory.MaxTitleLength;

    public static bool TryCreateKey(string? query, [NotNullWhen(true)] out string? key)
    {
        key = null;

        if (string.IsNullOrWhiteSpace(query) || query.Any(char.IsControl))
        {
            return false;
        }

        string trimmed = query.Trim();
        if (trimmed.Length > MaxQueryLength)
        {
            return false;
        }

        key = Fold(trimmed);
        return key.Length > 0;
    }

    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
