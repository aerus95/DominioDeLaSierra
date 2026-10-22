using System.Globalization;

namespace DominioDeLaSierra.Domain;

/// <summary>
/// Identificador numérico de Redsys, de 4 a 12 dígitos. No deriva del número DS- del pedido ni de la serie F2026.
/// La secuencia de base de datos empieza en 12 dígitos para no reutilizar valores cortos.
/// </summary>
public static class RedsysOperationNumber
{
    public const int MinLength = 4;
    public const int MaxLength = 12;
    public const long SequenceStart = 100_000_000_001;
    public const long SequenceEnd = 999_999_999_999;
    public const string InvalidMessage = "El número de operación de Redsys no es válido.";

    public static string Require(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length < MinLength || text.Length > MaxLength || !IsDigits(text))
        {
            throw new ArgumentException(InvalidMessage);
        }

        return text;
    }

    public static string FromSequence(long value)
    {
        if (value < 1_000 || value > SequenceEnd)
        {
            throw new ArgumentOutOfRangeException(nameof(value), InvalidMessage);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsDigits(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
