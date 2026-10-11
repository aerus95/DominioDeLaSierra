using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.UnitTests;

public class OrderNotificationTests
{
    private const string IntentId = "pi_test_notification01";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 11, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PaidAt = CreatedAt.AddMinutes(4);

    [Fact]
    public async Task Records_one_pending_confirmation_when_the_payment_is_confirmed()
    {
        var (order, payment, reservation) = PendingOrder();
        var stock = new StockProbe();

        await Confirm(order, payment, reservation, stock);

        var notification = Assert.Single(order.Notifications);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(1, stock.BeginCalls);
        Assert.Equal(1, stock.ConfirmCalls);
        Assert.Equal(order.Id, notification.OrderId);
        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(notification.Id.ToString("D"), notification.ProviderIdempotencyKey);
        Assert.Equal(OrderNotificationKind.PaidConfirmation, notification.Kind);
        Assert.Equal(OrderNotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.Attempts);
        Assert.Equal(PaidAt, notification.CreatedAt);
        Assert.Equal(PaidAt, notification.NextAttemptAt);
        Assert.Null(notification.SentAt);
        Assert.Null(notification.ProviderMessageId);
        Assert.Null(notification.LastError);
        Assert.Null(notification.ProcessingUntil);
    }

    [Fact]
    public async Task Does_not_record_another_confirmation_when_the_webhook_is_repeated()
    {
        var (order, payment, reservation) = PendingOrder();
        var stock = new StockProbe();
        await Confirm(order, payment, reservation, stock);
        reservation.BeginConfirmation(PaidAt);
        reservation.Confirm(PaidAt);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Confirm(order, payment, reservation, stock));

        Assert.Equal("El pedido no está pendiente de pago.", error.Message);
        Assert.Single(order.Notifications);
        Assert.Equal(1, stock.BeginCalls);
        Assert.Equal(1, stock.ConfirmCalls);
    }

    [Fact]
    public async Task Does_not_notify_a_payment_that_was_already_confirmed()
    {
        var (order, payment, reservation) = PendingOrder();
        reservation.BeginConfirmation(PaidAt);
        reservation.Confirm(PaidAt);
        order.MarkPaid(payment.Id, PaidAt);
        var settled = OrderReconciliationRules.Decide(SettledContext(
            order,
            payment,
            StockReservationStatus.Confirmed));
        var finishHold = OrderReconciliationRules.Decide(SettledContext(
            order,
            payment,
            StockReservationStatus.Confirming));

        Assert.Equal(OrderReconciliationDecision.AlreadySettled, settled.Decision);
        Assert.Equal(OrderReconciliationDecision.FinishHold, finishHold.Decision);
        Assert.Empty(order.Notifications);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Confirm(order, payment, reservation, new StockProbe()));

        Assert.Equal("El pedido no está pendiente de pago.", error.Message);
        Assert.Empty(order.Notifications);
    }

    [Fact]
    public async Task Does_not_record_a_confirmation_when_settlement_fails()
    {
        var mismatched = PendingOrder();
        var saved = false;
        try
        {
            await Confirm(mismatched.Order, mismatched.Payment, Confirmed(mismatched.Order), new StockProbe(), "pi_test_otherintent01");
            saved = true;
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal(StripeWebhookRules.IntentMessage, exception.Message);
        }

        Assert.False(saved);
        Assert.Equal(OrderStatus.PendingPayment, mismatched.Order.Status);
        Assert.Empty(mismatched.Order.Notifications);

        var (order, payment, reservation) = PendingOrder();
        var stock = new StockProbe { FailConfirmation = true };
        saved = false;
        try
        {
            await Confirm(order, payment, reservation, stock);
            saved = true;
        }
        catch (InvalidOperationException exception)
        {
            Assert.Equal("La reserva no admite la confirmación del pago.", exception.Message);
        }

        Assert.False(saved);
        Assert.Empty(order.Notifications);
        Assert.Equal(1, stock.BeginCalls);
        Assert.Equal(1, stock.ConfirmCalls);
    }

    [Fact]
    public async Task Keeps_a_single_confirmation_per_order_and_kind()
    {
        var (order, payment, reservation) = PendingOrder();
        await Confirm(order, payment, reservation, new StockProbe());
        var notification = Assert.Single(order.Notifications);

        var duplicate = Assert.Throws<InvalidOperationException>(() => order.RecordPaidConfirmation(PaidAt));
        Assert.Equal("La confirmación de pago no corresponde a una transición nueva.", duplicate.Message);
        Assert.Same(notification, Assert.Single(order.Notifications));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=dominio_migration_model;Username=none;Password=none")
            .Options;
        await using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(typeof(OrderNotification));
        Assert.NotNull(entity);
        var unique = Assert.Single(
            entity.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["OrderId", "Kind"]));
        Assert.Equal("OrderId", unique.Properties[0].Name);
        var concurrency = Assert.Single(entity.GetProperties(), property => property.IsConcurrencyToken);
        Assert.Equal("xmin", concurrency.GetColumnName());
    }

    [Fact]
    public void Does_not_open_a_confirmation_unless_the_order_has_just_been_paid()
    {
        var (order, payment, _) = PendingOrder();

        var beforePayment = Assert.Throws<InvalidOperationException>(() => order.RecordPaidConfirmation(PaidAt));
        Assert.Equal("La confirmación de pago no corresponde a una transición nueva.", beforePayment.Message);
        Assert.Empty(order.Notifications);

        order.MarkPaid(payment.Id, PaidAt);
        Assert.Empty(order.Notifications);
        order.RecordPaidConfirmation(PaidAt);
        Assert.Single(order.Notifications);
    }

    [Fact]
    public async Task Sends_once_and_keeps_the_same_provider_key_if_processing_is_interrupted()
    {
        var (order, payment, reservation) = PendingOrder();
        await Confirm(order, payment, reservation, new StockProbe());
        var notification = Assert.Single(order.Notifications);
        var key = notification.ProviderIdempotencyKey;

        notification.Claim(PaidAt);
        Assert.Equal(OrderNotificationStatus.Processing, notification.Status);
        Assert.Equal(PaidAt.Add(OrderNotificationRules.ProcessingLease), notification.ProcessingUntil);
        Assert.Equal(0, notification.Attempts);
        Assert.Throws<InvalidOperationException>(() => notification.Claim(notification.ProcessingUntil!.Value.AddTicks(-1)));

        var recoveredAt = notification.ProcessingUntil!.Value;
        notification.Claim(recoveredAt);
        var acceptedAt = notification.ProcessingUntil!.Value;
        Assert.Equal(key, notification.ProviderIdempotencyKey);
        Assert.Equal(0, notification.Attempts);
        notification.MarkSent(acceptedAt, "msg_provider_1");

        Assert.Equal(OrderNotificationStatus.Sent, notification.Status);
        Assert.Equal("msg_provider_1", notification.ProviderMessageId);
        Assert.Equal(acceptedAt, notification.SentAt);
        Assert.Null(notification.LastError);
        Assert.Null(notification.ProcessingUntil);
        Assert.Throws<InvalidOperationException>(() => notification.Claim(PaidAt.AddHours(1)));
    }

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 300)]
    [InlineData(3, 900)]
    [InlineData(4, 3_600)]
    [InlineData(5, 14_400)]
    [InlineData(6, 43_200)]
    [InlineData(7, 86_400)]
    public void Schedules_the_retry_delay(int failedAttempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), OrderNotificationRules.RetryDelay(failedAttempts));
    }

    [Fact]
    public async Task Abandons_after_eight_failures_and_stores_a_short_safe_error()
    {
        var (order, payment, reservation) = PendingOrder();
        await Confirm(order, payment, reservation, new StockProbe());
        var notification = Assert.Single(order.Notifications);
        var now = PaidAt;

        for (var attempt = 1; attempt <= OrderNotificationRules.MaxAttempts; attempt++)
        {
            notification.Claim(now);
            notification.RecordFailure(now, "fallo sk_test_abc123 para ana@example.com\n");
            Assert.Equal(attempt, notification.Attempts);
            Assert.Equal("fallo [omitido] para [correo]", notification.LastError);
            now = notification.NextAttemptAt ?? now;
        }

        Assert.Equal(OrderNotificationStatus.Abandoned, notification.Status);
        Assert.Null(notification.NextAttemptAt);
        Assert.Null(notification.ProcessingUntil);
        Assert.Throws<InvalidOperationException>(() => notification.Claim(now));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderNotificationRules.RetryDelay(OrderNotificationRules.MaxAttempts));
    }

    private static Task Confirm(
        Order order,
        Payment payment,
        StockReservation reservation,
        IOrderStock stock,
        string? intentId = null)
    {
        return new CheckoutSettlement(stock).ConfirmPaidAsync(
            order,
            payment,
            reservation,
            intentId ?? IntentId,
            PaidAt,
            CancellationToken.None);
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

    private static StockReservation Confirmed(Order order)
    {
        var reservation = StockReservation.Hold(Guid.NewGuid(), order.Id, CreatedAt);
        reservation.BeginConfirmation(PaidAt);
        reservation.Confirm(PaidAt);
        return reservation;
    }

    private static OrderReconciliationContext SettledContext(
        Order order,
        Payment payment,
        StockReservationStatus reservationStatus)
    {
        return new OrderReconciliationContext(
            order.Id,
            "cs_test_notification",
            IntentId,
            OrderStatus.Paid,
            PaymentStatus.Succeeded,
            order.TotalCents,
            payment.AmountCents,
            reservationStatus,
            order.ReservationExpiresAt,
            PaidAt,
            new ObservedCheckoutSession(
                "cs_test_notification",
                "complete",
                "paid",
                IntentId,
                "succeeded",
                order.TotalCents,
                "eur",
                false,
                order.Id.ToString("D"),
                order.Id.ToString("D")));
    }

    private sealed class StockProbe : IOrderStock
    {
        public bool FailConfirmation { get; init; }
        public int BeginCalls { get; private set; }
        public int ConfirmCalls { get; private set; }

        public Task ReserveAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task BeginPaymentConfirmationAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            BeginCalls++;
            return Task.CompletedTask;
        }

        public Task ConfirmPaymentAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default)
        {
            ConfirmCalls++;
            if (FailConfirmation)
            {
                throw new InvalidOperationException("La reserva no admite la confirmación del pago.");
            }

            return Task.CompletedTask;
        }
    }
}
