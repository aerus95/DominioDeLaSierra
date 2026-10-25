using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;

namespace DominioDeLaSierra.UnitTests;

public class OrderNotificationMailTests
{
    private const string IntentId = "pi_test_notification01";
    private const string MessageId = "11111111-1111-1111-1111-111111111111@mailpit.local";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 11, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PaidAt = CreatedAt.AddMinutes(4);

    [Fact]
    public void Renders_a_confirmation_without_customer_markup_or_payment_secrets()
    {
        var mail = PaidConfirmationMail.Compose(
            Content(name: "Ana\r\n<script>alert(1)</script>"),
            MessageId);

        Assert.Equal("Hemos recibido tu pedido DS-TEST-NOTIFY | Dominio de la Sierra", mail.Subject);
        Assert.DoesNotContain("\r", mail.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", mail.Subject, StringComparison.Ordinal);
        Assert.Contains("Ana &lt;script&gt;alert(1)&lt;/script&gt;", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("max-width:600px", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("viewport", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("#382029", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("DOM-1", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("17,00 €", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Hemos recibido tu pedido y confirmado el pago.", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("Pago confirmado", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("PayPal", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("IVA incluido en los productos: 2,95 €", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("IVA incluido en el envío: 1,39 €", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("No se suma otra vez.", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains(PaidConfirmationMail.StoreEmail, mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("8,00 €", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("25,00 €", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains("Calle Mayor 1", mail.TextBody, StringComparison.Ordinal);
        Assert.Contains(PaidConfirmationMail.ShippedLaterMessage, mail.TextBody, StringComparison.Ordinal);
        Assert.Contains(PaidConfirmationMail.ShippedLaterMessage, mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("whsec_", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("cs_", mail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckoutAccess", mail.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(MessageId, mail.MessageId);
    }

    [Theory]
    [InlineData("Live")]
    [InlineData("Brevo")]
    [InlineData("SendGrid")]
    [InlineData("Smtp")]
    public void Development_rejects_an_external_mail_provider(string delivery)
    {
        var options = LocalCapture();
        options.Delivery = delivery;

        var error = Assert.Throws<InvalidOperationException>(() =>
            OrderNotificationDeliveryGuard.Ensure("Development", options));

        Assert.Equal(OrderNotificationDeliveryGuard.ExternalDeliveryMessage, error.Message);
    }

    [Fact]
    public void Capture_must_stay_on_the_local_mailpit_port_without_credentials()
    {
        var remote = LocalCapture();
        remote.SmtpHost = "smtp.gmail.com";
        var submission = LocalCapture();
        submission.SmtpPort = 587;
        var credentials = LocalCapture();
        credentials.SmtpPassword = "secret";
        var production = LocalCapture();
        var tests = new OrderNotificationOptions();

        Assert.Equal(
            OrderNotificationDeliveryGuard.CaptureOnlyMessage,
            Assert.Throws<InvalidOperationException>(() => OrderNotificationDeliveryGuard.Ensure("Development", remote)).Message);
        Assert.Equal(
            OrderNotificationDeliveryGuard.CaptureOnlyMessage,
            Assert.Throws<InvalidOperationException>(() => OrderNotificationDeliveryGuard.Ensure("Development", submission)).Message);
        Assert.Equal(
            OrderNotificationDeliveryGuard.CredentialsMessage,
            Assert.Throws<InvalidOperationException>(() => OrderNotificationDeliveryGuard.Ensure("Development", credentials)).Message);
        Assert.Equal(
            OrderNotificationDeliveryGuard.CaptureOnlyMessage,
            Assert.Throws<InvalidOperationException>(() => OrderNotificationDeliveryGuard.Ensure("Production", production)).Message);
        OrderNotificationDeliveryGuard.Ensure("Development", LocalCapture());
        OrderNotificationDeliveryGuard.Ensure("Development", new OrderNotificationOptions());
        OrderNotificationDeliveryGuard.Ensure("Testing", tests);
    }

    [Fact]
    public async Task The_mailpit_sender_refuses_a_host_that_is_not_loopback()
    {
        var sender = new MailpitOrderMailSender(new OrderNotificationOptions
        {
            Delivery = OrderNotificationDelivery.Capture,
            SmtpHost = "smtp.gmail.com",
            SmtpPort = 1025
        });

        var error = await Assert.ThrowsAsync<OrderMailDeliveryException>(() =>
            sender.SendAsync(PaidConfirmationMail.Compose(Content(), MessageId)));

        Assert.Equal(OrderNotificationDeliveryGuard.CaptureOnlyMessage, error.Message);
    }

    [Fact]
    public async Task A_paid_order_without_a_notification_has_nothing_to_send()
    {
        var (order, payment, _) = PendingOrder();
        order.MarkPaid(payment.Id, PaidAt);

        Assert.Empty(order.Notifications);
        await Assert.ThrowsAsync<OrderMailDeliveryException>(() =>
            new DisabledOrderMailSender().SendAsync(PaidConfirmationMail.Compose(Content(), MessageId)));
    }

    [Fact]
    public async Task A_repeated_confirmation_does_not_create_a_second_notification()
    {
        var (order, payment, reservation) = PendingOrder();
        var settlement = new CheckoutSettlement(new StockProbe());
        await settlement.ConfirmPaidAsync(order, payment, reservation, IntentId, PaidAt, CancellationToken.None);
        reservation.BeginConfirmation(PaidAt);
        reservation.Confirm(PaidAt);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            settlement.ConfirmPaidAsync(order, payment, reservation, IntentId, PaidAt, CancellationToken.None));

        Assert.Equal("El pedido no está pendiente de pago.", error.Message);
        Assert.Single(order.Notifications);
    }

    [Fact]
    public async Task An_smtp_failure_keeps_the_payment_and_a_later_retry_can_finish()
    {
        var (order, payment, reservation) = PendingOrder();
        await new CheckoutSettlement(new StockProbe()).ConfirmPaidAsync(
            order, payment, reservation, IntentId, PaidAt, CancellationToken.None);
        var notification = Assert.Single(order.Notifications);
        notification.Claim(PaidAt);
        var mail = PaidConfirmationMail.Compose(Content(), $"{notification.Id:D}@mailpit.local");

        await Assert.ThrowsAsync<OrderMailDeliveryException>(() => new DisabledOrderMailSender().SendAsync(mail));
        notification.RecordFailure(PaidAt, "El aviso no se ha podido enviar.");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderNotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal(PaidAt.AddMinutes(1), notification.NextAttemptAt);

        notification.Claim(notification.NextAttemptAt!.Value);
        Assert.Equal(1, notification.Attempts);
        notification.MarkSent(notification.ProcessingUntil!.Value, $"{notification.Id:D}@mailpit.local");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderNotificationStatus.Sent, notification.Status);
        Assert.Null(notification.ProcessingUntil);
        Assert.Single(order.Notifications);
    }

    private static OrderNotificationOptions LocalCapture()
    {
        return new OrderNotificationOptions
        {
            DispatchEnabled = true,
            Delivery = OrderNotificationDelivery.Capture,
            SmtpHost = "127.0.0.1",
            SmtpPort = 1025
        };
    }

    private static PaidConfirmationContent Content(string name = "Ana Rivas")
    {
        return new PaidConfirmationContent(
            "DS-TEST-NOTIFY",
            name,
            "ana@example.com",
            [new PaidConfirmationLine("Dominium", "DOM-1", 1, 1_700, 1_700)],
            1_700,
            295,
            800,
            139,
            2_500,
            CreatedAt,
            "PayPal",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            "España",
            null);
    }

    private static (Order Order, Payment Payment, StockReservation Reservation) PendingOrder()
    {
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-TEST-NOTIFY",
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, 1_700, 21m, null)],
            CreatedAt);
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.AssignPaymentIntent(payment.Id, IntentId);
        return (order, payment, StockReservation.Hold(Guid.NewGuid(), order.Id, CreatedAt));
    }

    private sealed class StockProbe : IOrderStock
    {
        public Task ReserveAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReleaseAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BeginPaymentConfirmationAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ConfirmPaymentAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
