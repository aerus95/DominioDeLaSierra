namespace DominioDeLaSierra.Application.Checkout;

public static class OrderNotificationDelivery
{
    public const string Disabled = "Disabled";
    public const string Capture = "Capture";
}

public sealed class OrderNotificationOptions
{
    public const string SectionName = "Notifications";

    public bool DispatchEnabled { get; set; }

    public string Delivery { get; set; } = OrderNotificationDelivery.Disabled;

    public string SmtpHost { get; set; } = "127.0.0.1";

    public int SmtpPort { get; set; } = 1025;

    public int IntervalSeconds { get; set; } = 15;

    public int BatchSize { get; set; } = 10;

    public string? SmtpUsername { get; set; }

    public string? SmtpPassword { get; set; }
}

public sealed class OrderMailDeliveryException : Exception
{
    public OrderMailDeliveryException(string message) : base(message)
    {
    }
}

public interface IOrderMailSender
{
    Task<string> SendAsync(OrderMail mail, CancellationToken cancellationToken = default);
}

public sealed record OrderNotificationDispatchResult(int Claimed, int Sent, int Failed);

public interface IOrderNotificationDispatch
{
    Task<OrderNotificationDispatchResult> RunOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// El modo Capture solo habla con Mailpit en el bucle local. Development rechaza cualquier
/// proveedor externo. El proceso queda apagado mientras DispatchEnabled no sea true.
/// </summary>
public static class OrderNotificationDeliveryGuard
{
    public const string ExternalDeliveryMessage = "Development no puede usar un proveedor externo de correo.";
    public const string CaptureOnlyMessage = "El envío local solo puede capturar correo en Mailpit.";
    public const string CredentialsMessage = "El envío local no admite credenciales de correo.";
    public const string TestingMessage = "Las pruebas no envían correo.";
    public const string InvalidDeliveryMessage = "El modo de envío no es válido.";
    private static readonly HashSet<string> ExternalDeliveries = new(StringComparer.OrdinalIgnoreCase)
    {
        "Live",
        "Production",
        "Brevo",
        "SendGrid",
        "Ses",
        "Smtp",
        "External",
        "Relay"
    };

    public static void Ensure(string environmentName, OrderNotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var delivery = options.Delivery?.Trim() ?? OrderNotificationDelivery.Disabled;
        if (ExternalDeliveries.Contains(delivery))
        {
            throw new InvalidOperationException(ExternalDeliveryMessage);
        }

        if (!string.Equals(delivery, OrderNotificationDelivery.Disabled, StringComparison.Ordinal)
            && !string.Equals(delivery, OrderNotificationDelivery.Capture, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(InvalidDeliveryMessage);
        }

        if (!string.IsNullOrWhiteSpace(options.SmtpUsername) || !string.IsNullOrWhiteSpace(options.SmtpPassword))
        {
            throw new InvalidOperationException(CredentialsMessage);
        }

        if (IsTesting(environmentName))
        {
            if (options.DispatchEnabled || !string.Equals(delivery, OrderNotificationDelivery.Disabled, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(TestingMessage);
            }

            return;
        }

        var capture = string.Equals(delivery, OrderNotificationDelivery.Capture, StringComparison.Ordinal);
        if (capture && (!IsDevelopment(environmentName) || !IsMailpit(options)))
        {
            throw new InvalidOperationException(CaptureOnlyMessage);
        }

        if (options.DispatchEnabled && (!IsDevelopment(environmentName) || !capture))
        {
            throw new InvalidOperationException(CaptureOnlyMessage);
        }
    }

    private static bool IsMailpit(OrderNotificationOptions options)
    {
        var host = options.SmtpHost?.Trim() ?? string.Empty;
        return options.SmtpPort == 1025
            && host is "127.0.0.1" or "localhost" or "::1";
    }

    private static bool IsTesting(string environmentName) =>
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    private static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
}
