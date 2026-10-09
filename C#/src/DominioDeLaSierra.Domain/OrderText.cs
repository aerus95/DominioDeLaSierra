namespace DominioDeLaSierra.Domain;

internal static class OrderText
{
    public static string Require(string? value, int maxLength, string label)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            throw new ArgumentException($"{label} es obligatorio.");
        }

        if (text.Length > maxLength)
        {
            throw new ArgumentException($"{label} no puede superar {maxLength} caracteres.");
        }

        if (HasControl(text))
        {
            throw new ArgumentException($"{label} contiene caracteres no permitidos.");
        }

        return text;
    }

    public static string? Optional(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Require(value, maxLength, label);
    }

    public static string RequireEmail(string? value)
    {
        var email = Require(value, 254, "El correo electrónico");
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1 || email.Contains(' '))
        {
            throw new ArgumentException("El correo electrónico no es válido.");
        }

        return email;
    }

    public static string RequireToken(string? value, int maxLength, string label)
    {
        var text = Require(value, maxLength, label);
        if (text.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"{label} no es válido.");
        }

        return text;
    }

    public static void RequireId(Guid id, string label)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException($"{label} es obligatorio.");
        }
    }

    private static bool HasControl(string text)
    {
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}
