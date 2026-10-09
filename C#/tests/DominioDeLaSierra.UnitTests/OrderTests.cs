using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Charges_eight_euros_below_the_free_shipping_threshold()
    {
        var order = Place(Wine(9_999));

        Assert.Equal(9_999, order.ProductSubtotalCents);
        Assert.Equal(800, order.ShippingCents);
        Assert.Equal(10_799, order.TotalCents);
        Assert.Equal(OrderAmounts.Currency, order.Currency);
        Assert.Equal(OrderAmounts.CountryCode, order.CountryCode);
        Assert.Null(order.ShippingVatRate);
        Assert.Null(order.ShippingTaxableBaseCents);
        Assert.Null(order.ShippingVatCents);
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(10_001)]
    public void Ships_for_free_from_one_hundred_euros_of_products(long unitPriceCents)
    {
        var order = Place(Wine(unitPriceCents));

        Assert.Equal(0, order.ShippingCents);
        Assert.Equal(unitPriceCents, order.TotalCents);
    }

    [Fact]
    public void Uses_the_combined_product_subtotal_before_shipping()
    {
        var belowThreshold = Place(Wine(3_000), Wine(3_000));
        var atThreshold = Place(Wine(5_000), Wine(5_000));

        Assert.Equal(6_000, belowThreshold.ProductSubtotalCents);
        Assert.Equal(800, belowThreshold.ShippingCents);
        Assert.Equal(6_800, belowThreshold.TotalCents);
        Assert.Equal(10_000, atThreshold.ProductSubtotalCents);
        Assert.Equal(0, atThreshold.ShippingCents);
        Assert.Equal(10_000, atThreshold.TotalCents);
    }

    [Fact]
    public void Splits_the_gross_price_with_the_product_vat_rate()
    {
        var order = Place(Wine(1_890, vatRate: 21m));
        var line = Assert.Single(order.Items);

        Assert.Equal(1_890, line.LineTotalCents);
        Assert.Equal(1_562, line.TaxableBaseCents);
        Assert.Equal(328, line.VatCents);
        Assert.Equal(21m, line.VatRate);
        Assert.Equal(1_562, order.ProductTaxableBaseCents);
        Assert.Equal(328, order.ProductVatCents);
    }

    [Fact]
    public void Sums_line_tax_instead_of_rounding_the_order_once()
    {
        var order = Place(Wine(1_890, vatRate: 21m), Wine(1_000, vatRate: 10m));

        Assert.Equal(2_890, order.ProductSubtotalCents);
        Assert.Equal(1_562 + 909, order.ProductTaxableBaseCents);
        Assert.Equal(328 + 91, order.ProductVatCents);
        Assert.Equal(800, order.ShippingCents);
        Assert.Equal(3_690, order.TotalCents);
    }

    [Fact]
    public void Keeps_a_pack_composition_snapshot()
    {
        var componentId = Guid.NewGuid();
        var order = Place(new OrderLine(
            Guid.NewGuid(),
            "Caja paisaje",
            "PACK-1",
            ProductKind.Pack,
            2,
            5_400,
            21m,
            [new OrderLineComponent(componentId, "Dominium", "DOM-1", 3)]));
        var component = Assert.Single(Assert.Single(order.Items).Components);

        Assert.Equal(componentId, component.ComponentProductId);
        Assert.Equal("Dominium", component.Name);
        Assert.Equal("DOM-1", component.Reference);
        Assert.Equal(3, component.QuantityPerPack);
        Assert.Equal(CreatedAt.AddMinutes(30), order.ReservationExpiresAt);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void Rejects_a_pack_without_components()
    {
        var exception = Assert.Throws<ArgumentException>(() => Place(new OrderLine(
            Guid.NewGuid(),
            "Caja vacía",
            "PACK-0",
            ProductKind.Pack,
            1,
            1_000,
            21m,
            null)));

        Assert.Equal("Un pack debe incluir al menos un componente.", exception.Message);
    }

    [Fact]
    public void Rejects_components_on_a_wine()
    {
        var exception = Assert.Throws<ArgumentException>(() => Place(new OrderLine(
            Guid.NewGuid(),
            "Dominium",
            "DOM-1",
            ProductKind.Wine,
            1,
            1_000,
            21m,
            [new OrderLineComponent(Guid.NewGuid(), "Otro", "OTRO", 1)])));

        Assert.Equal("Solo un pack puede guardar componentes.", exception.Message);
    }

    [Fact]
    public void Rejects_a_repeated_product_and_a_repeated_component()
    {
        var productId = Guid.NewGuid();
        Assert.Equal(
            "El producto está repetido en el pedido.",
            Assert.Throws<ArgumentException>(() => Place(Wine(1_000, productId: productId), Wine(1_000, productId: productId))).Message);

        var componentId = Guid.NewGuid();
        Assert.Equal(
            "Hay un componente repetido.",
            Assert.Throws<ArgumentException>(() => Place(new OrderLine(
                Guid.NewGuid(),
                "Caja",
                "PACK-2",
                ProductKind.Pack,
                1,
                1_000,
                21m,
                [
                    new OrderLineComponent(componentId, "Uno", "UNO", 1),
                    new OrderLineComponent(componentId, "Uno", "UNO", 2)
                ]))).Message);
    }

    [Fact]
    public void Defines_shipping_vat_without_changing_the_gross_total()
    {
        var order = Place(Wine(1_000));
        order.DefineShippingVat(21m);

        Assert.Equal(21m, order.ShippingVatRate);
        Assert.Equal(661, order.ShippingTaxableBaseCents);
        Assert.Equal(139, order.ShippingVatCents);
        Assert.Equal(1_800, order.TotalCents);

        Assert.Equal(
            "El IVA del envío ya está definido.",
            Assert.Throws<InvalidOperationException>(() => order.DefineShippingVat(21m)).Message);
    }

    [Fact]
    public void Does_not_mark_an_order_paid_without_a_pending_payment()
    {
        var order = Place(Wine(1_890));

        Assert.Equal(
            "El pago no pertenece al pedido.",
            Assert.Throws<InvalidOperationException>(() => order.MarkPaid(Guid.NewGuid(), CreatedAt)).Message);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
    }

    [Fact]
    public void Marks_paid_only_through_the_payment_transition()
    {
        var order = Place(Wine(1_890));
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.AssignCheckoutSession(payment.Id, "cs_test_checkout");
        order.AssignPaymentIntent(payment.Id, "pi_test_intent");
        var paymentEvent = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "evt_test_paid", "checkout.session.completed", CreatedAt);

        order.MarkPaid(payment.Id, CreatedAt.AddMinutes(1));
        order.MarkPaymentEventProcessed(payment.Id, paymentEvent.ExternalEventId, CreatedAt.AddMinutes(1));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(order.TotalCents, payment.AmountCents);
        Assert.Equal("cs_test_checkout", payment.StripeCheckoutSessionId);
        Assert.Equal("pi_test_intent", payment.StripePaymentIntentId);
        Assert.NotNull(paymentEvent.ProcessedAt);
        Assert.Equal(
            "El pedido no está pendiente de pago.",
            Assert.Throws<InvalidOperationException>(() => order.Cancel(CreatedAt.AddMinutes(2))).Message);
    }

    [Fact]
    public void Keeps_the_order_pending_when_a_payment_fails_and_allows_another_attempt()
    {
        var order = Place(Wine(1_890));
        var failed = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.RecordPaymentFailure(failed.Id, CreatedAt.AddMinutes(1));
        var replacement = order.AddPayment(Guid.NewGuid(), CreatedAt.AddMinutes(2));

        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(PaymentStatus.Failed, failed.Status);
        Assert.Equal(PaymentStatus.Pending, replacement.Status);
        Assert.Equal(
            "El pedido ya tiene un pago pendiente.",
            Assert.Throws<InvalidOperationException>(() => order.AddPayment(Guid.NewGuid(), CreatedAt)).Message);
    }

    [Fact]
    public void Expires_only_after_thirty_minutes_and_cancels_the_pending_payment()
    {
        var order = Place(Wine(1_890));
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);

        Assert.Equal(
            "La reserva todavía no ha caducado.",
            Assert.Throws<InvalidOperationException>(() => order.Expire(order.ReservationExpiresAt.AddTicks(-1))).Message);

        order.Expire(order.ReservationExpiresAt);

        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(order.ReservationExpiresAt, order.ExpiredAt);
    }

    [Fact]
    public void Records_a_refund_without_changing_the_paid_order()
    {
        var order = Place(Wine(1_890));
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.MarkPaid(payment.Id, CreatedAt);
        order.RecordRefund(payment.Id, CreatedAt.AddMinutes(5));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.NotNull(payment.SucceededAt);
        Assert.NotNull(payment.RefundedAt);
    }

    [Fact]
    public void Rejects_a_repeated_external_payment_event()
    {
        var order = Place(Wine(1_890));
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "evt_once", "checkout.session.completed", CreatedAt);

        Assert.Equal(
            "El evento de pago ya está registrado.",
            Assert.Throws<InvalidOperationException>(() =>
                order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), "evt_once", "checkout.session.completed", CreatedAt)).Message);
    }

    [Fact]
    public void Rejects_shipping_vat_after_a_payment_exists()
    {
        var order = Place(Wine(1_000));
        order.AddPayment(Guid.NewGuid(), CreatedAt);

        Assert.Equal(
            "El IVA del envío debe definirse antes de iniciar el pago.",
            Assert.Throws<InvalidOperationException>(() => order.DefineShippingVat(21m)).Message);
    }

    private static Order Place(params OrderLine[] lines)
    {
        return Order.CreatePending(
            Guid.NewGuid(),
            "DS-TEST-1",
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            lines,
            CreatedAt);
    }

    private static OrderLine Wine(long unitPriceCents, int quantity = 1, decimal vatRate = 21m, Guid? productId = null)
    {
        return new OrderLine(
            productId ?? Guid.NewGuid(),
            "Dominium",
            "DOM-1",
            ProductKind.Wine,
            quantity,
            unitPriceCents,
            vatRate,
            null);
    }
}
