using System.Text.RegularExpressions;

namespace DominioDeLaSierra.Application.Common;

public static partial class AdminAccountRules
{
    public const int UsernameMaxLength = 80;
    public const int DisplayNameMaxLength = 150;
    public const int NewPasswordMinLength = 12;
    public const int PasswordMaxLength = 128;
    public const int PasswordHashMaxLength = 500;

    public static string NormalizeUsername(string? username)
    {
        var value = username?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        }

        if (value.Length > UsernameMaxLength)
        {
            throw new ArgumentException("El nombre de usuario no puede superar 80 caracteres.");
        }

        if (!UsernamePattern().IsMatch(value))
        {
            throw new ArgumentException("El nombre de usuario solo puede contener letras, números, puntos, guiones y guiones bajos.");
        }

        return value;
    }

    public static string NormalizeDisplayName(string? displayName)
    {
        var value = displayName?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new ArgumentException("El nombre visible es obligatorio.");
        }

        if (value.Length > DisplayNameMaxLength)
        {
            throw new ArgumentException("El nombre visible no puede superar 150 caracteres.");
        }

        if (CatalogText.ContainsDisallowedCharacters(value, allowLineBreaks: false))
        {
            throw new ArgumentException("El nombre visible contiene caracteres no permitidos.");
        }

        return value;
    }

    public static void EnsureNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("La contraseña es obligatoria.");
        }

        if (password.Length < NewPasswordMinLength || password.Length > PasswordMaxLength)
        {
            throw new ArgumentException("La contraseña debe tener entre 12 y 128 caracteres.");
        }
    }

    public static bool IsWithinLoginLimits(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        return username.Trim().Length <= UsernameMaxLength && password.Length <= PasswordMaxLength;
    }

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
