using System.Text;
using System.Text.RegularExpressions;

namespace DominioDeLaSierra.Domain;

/// <summary>
/// Reglas del aviso pendiente. Deciden cuándo se puede tomar, cuándo se reintenta
/// y cuándo se abandona sin tocar el pago.
/// La clave de idempotencia del proveedor es el identificador de la notificación.
/// Si el proveedor acepta el mensaje y el proceso cae antes de marcarlo enviado,
/// el reintento presenta la misma clave.
/// </summary>
public static partial class OrderNotificationRules
{
    public const int MaxAttempts = 8;
    public const int LastErrorMaxLength = 240;
    public const int ProviderMessageIdMaxLength = 255;
    public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(2);

    public static string ProviderIdempotencyKey(Guid notificationId)
    {
        OrderText.RequireId(notificationId, "La notificación");
        return notificationId.ToString("D");
    }

    public static bool CanClaim(
        OrderNotificationStatus status,
        DateTimeOffset? processingUntil,
        DateTimeOffset? nextAttemptAt,
        DateTimeOffset now)
    {
        return status switch
        {
            OrderNotificationStatus.Pending => nextAttemptAt is not null && nextAttemptAt <= now,
            OrderNotificationStatus.Failed => nextAttemptAt is not null && nextAttemptAt <= now,
            OrderNotificationStatus.Processing => processingUntil is not null && processingUntil <= now,
            _ => false
        };
    }

    public static TimeSpan RetryDelay(int failedAttempts)
    {
        if (failedAttempts < 1 || failedAttempts >= MaxAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(failedAttempts), "El intento de reenvío no es válido.");
        }

        var seconds = failedAttempts switch
        {
            1 => 60,
            2 => 300,
            3 => 900,
            4 => 3_600,
            5 => 14_400,
            6 => 43_200,
            _ => 86_400
        };
        return TimeSpan.FromSeconds(seconds);
    }

    public static string SanitizeError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("El error del envío es obligatorio.");
        }

        var compact = new StringBuilder(error.Length);
        var pendingSpace = false;
        foreach (var character in error.Trim())
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                pendingSpace = compact.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                compact.Append(' ');
                pendingSpace = false;
            }

            compact.Append(character);
        }

        var text = SecretPattern().Replace(compact.ToString(), "[omitido]");
        text = EmailPattern().Replace(text, "[correo]");
        text = text.Trim();
        if (text.Length == 0)
        {
            text = "El envío falló.";
        }

        if (text.Length > LastErrorMaxLength)
        {
            text = text[..LastErrorMaxLength];
        }

        return text;
    }

    [GeneratedRegex(@"(?i)(?:sk|rk)_(?:live|test)_[A-Za-z0-9]+|whsec_[A-Za-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
