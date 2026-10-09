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
