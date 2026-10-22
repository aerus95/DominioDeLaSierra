using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Crea una orden CAPTURE a partir del pedido ya guardado. El importe y la moneda salen de ese pedido.
/// PayPal-Request-Id se guarda antes del POST. Si la respuesta se pierde, no se repite el POST:
/// PayPal solo documenta la repetición durante 6 horas y con el mismo cuerpo, y no publica un GET por esa clave.
/// CheckoutExpiresAt es la reserva de stock, no una caducidad devuelta por PayPal.
/// </summary>
public sealed class PayPalPaymentStarter(
    ApplicationDbContext dbContext,
    IPayPalOrders paypal,
    PayPalSettings settings)
{
    public async Task<CheckoutPaymentDto> StartAsync(GuestOrderDto order, CancellationToken cancellationToken)
    {
        if (!settings.IsOperational)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }

        if (!string.Equals(order.Currency, OrderAmounts.Currency, StringComparison.Ordinal) || order.TotalCents <= 0)
        {
            throw new InvalidOperationException(CheckoutLimits.PaymentUnavailableMessage);
        }

        var prepared = await PrepareAsync(order, cancellationToken);
        if (prepared.Existing is not null)
        {
            return prepared.Existing;
        }

        if (prepared.Refusal is not null)
        {
            throw new InvalidOperationException(prepared.Refusal);
        }

        var requestId = PayPalProtocol.CreateRequestId(prepared.PaymentId);
        PayPalOrderSnapshot created;
        if (prepared.KnownOrderId is not null)
        {
            created = await paypal.GetAsync(prepared.KnownOrderId, cancellationToken);
        }
        else
        {
            try
            {
                created = await paypal.CreateAsync(prepared.Command, requestId, cancellationToken);
            }
            catch (PayPalTransportException)
            {
                await NoteUncertainAsync(order.OrderId, prepared.PaymentId, cancellationToken);
                throw;
            }
        }

        return await StoreAsync(order, prepared.PaymentId, created, cancellationToken);
    }

    private async Task<PreparedPayPalOrder> PrepareAsync(GuestOrderDto order, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(order.OrderId, cancellationToken))
        {
            throw new CheckoutNotFoundException();
        }

        var stored = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == order.OrderId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        EnsurePayable(stored, now);
        if (stored.TotalCents != order.TotalCents || !string.Equals(stored.Currency, order.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(PayPalRules.AmountMessage);
        }

        var pending = stored.Payments.SingleOrDefault(payment => payment.Status == PaymentStatus.Pending);
        if (pending is not null && pending.Provider != PaymentProvider.PayPal)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }

        if (pending?.CheckoutUrl is not null)
        {
            var existing = Present(stored, order.CheckoutAccessToken);
            await transaction.CommitAsync(cancellationToken);
            return new PreparedPayPalOrder(pending.Id, Command(stored), existing, null, null);
        }

        if (pending is null)
        {
            pending = stored.AddPayment(Guid.NewGuid(), now, PaymentProvider.PayPal);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var command = Command(stored);
        if (pending.ExternalOperationId is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new PreparedPayPalOrder(pending.Id, command, null, null, pending.ExternalOperationId);
        }

        var block = pending.CreateBlock(now);
        if (!PayPalProtocol.MayPostCreate(block, externalOrderIdKnown: false))
        {
            var refusal = block == PayPalCreateBlock.OutsideDocumentedWindow
                ? PayPalRules.ExpiredIdempotencyMessage
                : PayPalRules.UncertainCreateMessage;
            await transaction.CommitAsync(cancellationToken);
            return new PreparedPayPalOrder(pending.Id, command, null, refusal, null);
        }

        stored.BeginPayPalCreate(pending.Id, PayPalProtocol.CreateRequestId(pending.Id), PayPalProtocol.RequestFingerprint(command), now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PreparedPayPalOrder(pending.Id, command, null, null, null);
    }

    private async Task<CheckoutPaymentDto> StoreAsync(
        GuestOrderDto order,
        Guid paymentId,
        PayPalOrderSnapshot created,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(order.OrderId, cancellationToken))
        {
            throw new CheckoutNotFoundException();
        }

        var stored = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == order.OrderId, cancellationToken);
        var pending = stored.Payments.Single(payment => payment.Id == paymentId);
        if (pending.CheckoutUrl is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Present(stored, order.CheckoutAccessToken);
        }

        var sameOperation = pending.ExternalOperationId is null
            || string.Equals(created.Id, pending.ExternalOperationId, StringComparison.Ordinal);
        if (!sameOperation || !PayPalProtocol.IsOrderId(created.Id))
        {
            Remember(stored, pending, PayPalRules.IdentityMessage, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new InvalidOperationException(PayPalRules.IdentityMessage);
        }

        if (pending.ExternalOperationId is null)
        {
            stored.RememberPayPalOrder(pending.Id, created.Id);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await AttachApprovalAsync(order, paymentId, created, cancellationToken);
    }

    private async Task<CheckoutPaymentDto> AttachApprovalAsync(
        GuestOrderDto order,
        Guid paymentId,
        PayPalOrderSnapshot created,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(order.OrderId, cancellationToken))
        {
            throw new CheckoutNotFoundException();
        }

        var stored = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == order.OrderId, cancellationToken);
        var pending = stored.Payments.Single(payment => payment.Id == paymentId);
        if (pending.CheckoutUrl is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Present(stored, order.CheckoutAccessToken);
        }

        var reading = PayPalProtocol.Interpret(created, stored.Id, stored.Number, stored.TotalCents);
        if (!PayPalProtocol.IsSandboxPayerRedirect(created, reading))
        {
            Remember(stored, pending, reading.Attention ?? PayPalRules.ApprovalMessage, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new InvalidOperationException(reading.Attention ?? PayPalRules.ApprovalMessage);
        }

        stored.AttachPayPalApproval(pending.Id, created.Id, created.ApproveUrl!, stored.ReservationExpiresAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Present(stored, order.CheckoutAccessToken);
    }

    private async Task NoteUncertainAsync(Guid orderId, Guid paymentId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            return;
        }

        var stored = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        stored.NotePayPalCreateUncertain(paymentId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private PayPalCreateCommand Command(Order stored)
    {
        return new PayPalCreateCommand(
            stored.Id,
            stored.Number,
            stored.TotalCents,
            stored.CustomerName,
            stored.AddressLine,
            stored.PostalCode,
            stored.City,
            stored.Province,
            settings.ReturnUrl,
            settings.CancelUrl);
    }

    private static CheckoutPaymentDto Present(Order stored, string? accessToken)
    {
        var pending = stored.Payments.Single(payment => payment.Status == PaymentStatus.Pending);
        return new CheckoutPaymentDto(
            stored.Id,
            stored.Number,
            stored.ProductSubtotalCents,
            stored.ShippingCents,
            stored.ShippingVatRate,
            stored.TotalCents,
            stored.Currency,
            stored.ReservationExpiresAt,
            stored.Status.ToString(),
            stored.ShippingVatRate is null,
            pending.CheckoutUrl!,
            pending.CheckoutExpiresAt!.Value,
            accessToken,
            nameof(PaymentProvider.PayPal),
            "GET");
    }

    private static void Remember(Order order, Payment payment, string attention, DateTimeOffset now)
    {
        var eventId = PayPalCreateMarkers.EventId(payment.Id);
        if (payment.Events.Any(item => item.ExternalEventId == eventId))
        {
            return;
        }

        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), eventId, "paypal.order.create", now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, attention);
    }

    private static void EnsurePayable(Order order, DateTimeOffset now)
    {
        if (order.Status == OrderStatus.Paid)
        {
            throw new InvalidOperationException(CheckoutLimits.PaidOrderMessage);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException(CheckoutLimits.CancelledOrderMessage);
        }

        if (order.Status == OrderStatus.Expired || now >= order.ReservationExpiresAt)
        {
            throw new InvalidOperationException(StripeCheckoutExpiry.ExpiredMessage);
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException(OrderStockRules.NotPendingMessage);
        }
    }

    private async Task<bool> LockOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException(OrderStockRules.MissingOrderMessage);
        command.CommandText = """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = orderId;
        command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private sealed record PreparedPayPalOrder(
        Guid PaymentId,
        PayPalCreateCommand Command,
        CheckoutPaymentDto? Existing,
        string? Refusal,
        string? KnownOrderId);
}
