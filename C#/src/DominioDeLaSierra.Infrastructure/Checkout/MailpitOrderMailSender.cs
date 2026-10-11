using DominioDeLaSierra.Application.Checkout;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public sealed class DisabledOrderMailSender : IOrderMailSender
{
    public Task<string> SendAsync(OrderMail mail, CancellationToken cancellationToken = default)
    {
        throw new OrderMailDeliveryException("El envío de avisos está desactivado.");
    }
}

/// <summary>
/// Entrega el mensaje solo a Mailpit. No autentica y no abre TLS: el guardián ya exigió
/// 127.0.0.1:1025. SMTP no deduplica; si el proceso cae después de aceptar el mensaje
/// y antes de marcarlo enviado, un reintento puede dejar otra copia en la bandeja.
/// </summary>
public sealed class MailpitOrderMailSender(OrderNotificationOptions options) : IOrderMailSender
{
    public const string FromAddress = "pedidos@mailpit.local";

    public async Task<string> SendAsync(OrderMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (!string.Equals(options.Delivery, OrderNotificationDelivery.Capture, StringComparison.Ordinal)
            || options.SmtpPort != 1025
            || options.SmtpHost is not ("127.0.0.1" or "localhost" or "::1"))
        {
            throw new OrderMailDeliveryException(OrderNotificationDeliveryGuard.CaptureOnlyMessage);
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Dominio de la Sierra", FromAddress));
        try
        {
            message.To.Add(MailboxAddress.Parse(mail.Recipient));
        }
        catch (ParseException)
        {
            throw new OrderMailDeliveryException("El correo del pedido no es válido.");
        }

        message.Subject = mail.Subject;
        message.MessageId = mail.MessageId;
        message.Headers.Add("X-Dominio-Notification", mail.MessageId);
        var body = new BodyBuilder
        {
            TextBody = mail.TextBody,
            HtmlBody = mail.HtmlBody
        };
        message.Body = body.ToMessageBody();

        using var client = new SmtpClient
        {
            Timeout = 10_000
        };
        try
        {
            await client.ConnectAsync(options.SmtpHost, options.SmtpPort, SecureSocketOptions.None, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OrderMailDeliveryException)
        {
            throw new OrderMailDeliveryException("El servidor SMTP no ha aceptado el mensaje.");
        }

        return mail.MessageId;
    }
}
