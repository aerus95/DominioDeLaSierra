namespace DominioDeLaSierra.Application.Checkout;

public static class StripeCheckoutGuard
{
    public const string TestKeyMessage = "Stripe solo está habilitado con una clave de pruebas.";
    public const string TestWebhookSecretMessage = "Stripe solo acepta un secreto de webhook de pruebas.";

    public static string RequireTestSecret(string? secret)
    {
        var key = secret?.Trim() ?? string.Empty;
        if (key.Length == 0
            || !key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.Contains("live", StringComparison.OrdinalIgnoreCase)
            || key.Any(char.IsWhiteSpace))
        {
            throw new StripeCheckoutException(TestKeyMessage);
        }

        return key;
    }

    public static string RequireTestWebhookSecret(string? secret)
    {
        var key = secret?.Trim() ?? string.Empty;
        if (key.Length < 12
            || !key.StartsWith("whsec_", StringComparison.Ordinal)
            || key.Contains("live", StringComparison.OrdinalIgnoreCase)
            || key.Any(char.IsWhiteSpace))
        {
            throw new StripeCheckoutException(TestWebhookSecretMessage);
        }

        return key;
    }

    public static bool IsLiveObjectId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("cs_live_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("pi_live_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("sk_live_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTestPaymentIntent(string? paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId) || paymentIntentId.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (IsLiveObjectId(paymentIntentId) || !paymentIntentId.StartsWith("pi_", StringComparison.Ordinal))
        {
            return false;
        }

        var body = paymentIntentId.AsSpan(3);
        if (body.IsEmpty)
        {
            return false;
        }

        foreach (var character in body)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class StripeCheckoutException : Exception
{
    public StripeCheckoutException(string message) : base(message)
    {
    }
}

public sealed class StripeWebhookSignatureException : Exception
{
    public StripeWebhookSignatureException()
        : base("La firma del webhook no es válida.")
    {
    }
}

public sealed class CheckoutNotFoundException : Exception
{
    public CheckoutNotFoundException()
        : base("El pedido no está disponible.")
    {
    }
}
