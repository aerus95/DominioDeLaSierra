using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DominioDeLaSierra.Application.Common;

public static class CatalogText
{
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(character);
        }

        var ascii = builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        ascii = Regex.Replace(ascii, @"[^a-z0-9]+", "-");
        return ascii.Trim('-');
    }

    public static bool ContainsDisallowedCharacters(string value, bool allowLineBreaks)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                return true;
            }

            if (!char.IsControl(character))
            {
                continue;
            }

            if (allowLineBreaks && character is '\r' or '\n' or '\t')
            {
                continue;
            }

            return true;
        }

        return false;
    }

    public static string RequireSingleLine(
        string? value,
        int maxLength,
        string requiredMessage,
        string lengthMessage,
        string charactersMessage)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ArgumentException(requiredMessage);
        }

        if (text.Length > maxLength)
        {
            throw new ArgumentException(lengthMessage);
        }

        if (ContainsDisallowedCharacters(text, allowLineBreaks: false))
        {
            throw new ArgumentException(charactersMessage);
        }

        return text;
    }

    public static string NormalizeOptionalText(
        string? value,
        int maxLength,
        bool allowLineBreaks,
        string lengthMessage,
        string charactersMessage)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length > maxLength)
        {
            throw new ArgumentException(lengthMessage);
        }

        if (ContainsDisallowedCharacters(text, allowLineBreaks))
        {
            throw new ArgumentException(charactersMessage);
        }

        return text;
    }

    public static string RequireSlug(
        string? requested,
        string fallbackName,
        int maxLength,
        string invalidMessage,
        string lengthMessage)
    {
        if (requested is not null && ContainsDisallowedCharacters(requested, allowLineBreaks: false))
        {
            throw new ArgumentException(invalidMessage);
        }

        var slug = string.IsNullOrWhiteSpace(requested)
            ? Slugify(fallbackName)
            : Slugify(requested);
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException(invalidMessage);
        }

        if (slug.Length > maxLength)
        {
            throw new ArgumentException(lengthMessage);
        }

        return slug;
    }
}
