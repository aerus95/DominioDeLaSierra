using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.UnitTests;

public class FulfillmentTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Paid_order_advances_one_step_at_a_time_and_keeps_an_audit()
    {
        var order = Paid();
        var preparingAt = CreatedAt.AddMinutes(5);
        var preparedAt = CreatedAt.AddMinutes(6);
        var shippedAt = CreatedAt.AddMinutes(7);

        order.AdvanceFulfillment(FulfillmentStatus.Preparing, preparingAt, ActorId, null, null);
        order.AdvanceFulfillment(FulfillmentStatus.Prepared, preparedAt, ActorId, "Seur", null);
        order.AdvanceFulfillment(FulfillmentStatus.Shipped, shippedAt, ActorId, null, "SEG-100");

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(FulfillmentStatus.Shipped, order.FulfillmentStatus);
        Assert.Equal("Seur", order.Carrier);
        Assert.Equal("SEG-100", order.TrackingNumber);
        Assert.Equal(shippedAt, order.FulfillmentUpdatedAt);
        Assert.Equal(ActorId, order.FulfillmentUpdatedByUserId);
        Assert.Equal(3, order.FulfillmentTransitions.Count);
        Assert.Equal(FulfillmentStatus.Unfulfilled, order.FulfillmentTransitions[0].FromStatus);
        Assert.Equal(FulfillmentStatus.Shipped, order.FulfillmentTransitions[2].ToStatus);
        Assert.Equal(ActorId, order.FulfillmentTransitions[2].ActorUserId);
    }

    [Fact]
    public void Unpaid_order_cannot_be_prepared()
    {
        var order = Pending();

        var error = Assert.Throws<InvalidOperationException>(() =>
            order.AdvanceFulfillment(FulfillmentStatus.Preparing, CreatedAt.AddMinutes(1), ActorId, null, null));

        Assert.Equal("Solo se puede preparar un pedido pagado.", error.Message);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.Equal(FulfillmentStatus.Unfulfilled, order.FulfillmentStatus);
        Assert.Empty(order.FulfillmentTransitions);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Preparing)]
    [InlineData(FulfillmentStatus.Shipped)]
    public void Rejects_duplicate_and_skipped_transitions(FulfillmentStatus target)
    {
        var order = Paid();
        order.AdvanceFulfillment(FulfillmentStatus.Preparing, CreatedAt.AddMinutes(1), ActorId, null, null);
        var before = order.FulfillmentTransitions.Count;

        var error = Assert.Throws<InvalidOperationException>(() =>
            order.AdvanceFulfillment(target, CreatedAt.AddMinutes(2), ActorId, null, null));

        Assert.Equal(
            target == FulfillmentStatus.Preparing
                ? "El pedido ya está en ese estado de preparación."
                : "El pedido no puede pasar a ese estado de preparación.",
            error.Message);
        Assert.Equal(before, order.FulfillmentTransitions.Count);
        Assert.Equal(FulfillmentStatus.Preparing, order.FulfillmentStatus);
    }

    [Fact]
    public void Fulfillment_does_not_change_payment_or_amounts()
    {
        var order = Paid();
        var total = order.TotalCents;
        var paidAt = order.PaidAt;

        order.AdvanceFulfillment(FulfillmentStatus.Preparing, CreatedAt.AddMinutes(1), ActorId, null, null);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(paidAt, order.PaidAt);
        Assert.Equal(total, order.TotalCents);
        Assert.Equal(PaymentStatus.Succeeded, order.Payments.Single().Status);
    }

    private static Order Paid()
    {
        var order = Pending();
        var payment = order.AddPayment(Guid.NewGuid(), CreatedAt);
        order.MarkPaid(payment.Id, CreatedAt.AddMinutes(1));
        return order;
    }

    private static Order Pending()
    {
        return Order.CreatePending(
            Guid.NewGuid(),
            "DS-FULFILL-1",
            Guid.NewGuid().ToString("N"),
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [new OrderLine(Guid.NewGuid(), "Dominium", "DOM-1", ProductKind.Wine, 1, 1_890, 21m, null)],
            CreatedAt);
    }
}
