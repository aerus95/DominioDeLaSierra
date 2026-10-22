using System.Data;
using System.Text.Json;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// La captura y el webhook confirmados comparten el mismo cierre. El retorno del navegador no cobra:
/// solo pide esta captura, y el cobro sale del recurso de PayPal.
/// Una orden APPROVED sigue siendo cobrable. Un estado desconocido conserva el stock.
/// </summary>
public sealed class PayPalCheckoutService(
    ApplicationDbContext dbContext,
    IPayPalOrders paypal,
    IPayPalCertificates certificates,
    IOrderStock orderStock,
    PayPalSettings settings,
    ReservationHoldIncidentRecorder incidents,
    ILogger<PayPalCheckoutService> logger) : IPayPalCaptures, IPayPalWebhooks, IPayPalObservations
{
    public async Task<CheckoutOutcomeDto> CaptureAsync(
        Guid orderId,
        string? checkoutAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsOperational || orderId == Guid.Empty || string.IsNullOrWhiteSpace(checkoutAccessToken))
        {
            throw new CheckoutNotFoundException();
        }

        string hash;
        try
        {
            hash = CheckoutAccessToken.Hash(checkoutAccessToken);
        }
        catch (ArgumentException)
        {
            throw new CheckoutNotFoundException();
        }

        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            throw new CheckoutNotFoundException();
        }

        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        if (!CheckoutAccessToken.FixedEquals(order.CheckoutAccessTokenHash, hash))
        {
            throw new CheckoutNotFoundException();
        }

        var payment = order.Payments.SingleOrDefault(item => item.Provider == PaymentProvider.PayPal && item.ExternalOperationId is not null);
        if (payment is null || payment.Status != PaymentStatus.Pending || order.Status != OrderStatus.PendingPayment)
        {
            await transaction.CommitAsync(cancellationToken);
            return CheckoutOutcomeRules.Present(order.Number, order.Status, order.TotalCents, order.Currency);
        }

        var snapshot = await CurrentAsync(order, payment, captureIfApproved: true, cancellationToken);
        var reading = PayPalProtocol.Interpret(snapshot, order.Id, order.Number, order.TotalCents);
        await ApplyAsync(order, payment, reading, EventId(reading, payment), "orders.capture", DateTimeOffset.UtcNow, observe: false, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CheckoutOutcomeRules.Present(order.Number, order.Status, order.TotalCents, order.Currency);
    }

    public async Task ReceiveAsync(
        PayPalTransmission transmission,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsOperational || body.Length == 0 || body.Length > PayPalRules.MaxWebhookBytes)
        {
            throw new PayPalSignatureException();
        }

        var pem = await certificates.ReadPemAsync(transmission.CertificateUrl ?? string.Empty, cancellationToken);
        if (!PayPalProtocol.Verify(transmission, body, settings.WebhookId, pem))
        {
            throw new PayPalSignatureException();
        }

        PayPalEventNotice? notice;
        try
        {
            using var document = JsonDocument.Parse(body);
            notice = PayPalProtocol.ReadEvent(document.RootElement);
        }
        catch (JsonException)
        {
            logger.LogInformation("Webhook PayPal firmado sin un recurso de orden reconocible.");
            return;
        }

        if (notice is null)
        {
            logger.LogInformation("Webhook PayPal firmado sin un identificador de orden.");
            return;
        }

        dbContext.ChangeTracker.Clear();
        var located = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Provider == PaymentProvider.PayPal && payment.ExternalOperationId == notice.PaypalOrderId)
            .Select(payment => new { payment.OrderId, payment.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null && notice.LocalOrderId is Guid localOrderId)
        {
            located = await dbContext.Payments
                .AsNoTracking()
                .Where(payment => payment.OrderId == localOrderId
                    && payment.Provider == PaymentProvider.PayPal
                    && payment.Status == PaymentStatus.Pending
                    && payment.ExternalOperationId == null)
                .Select(payment => new { payment.OrderId, payment.Id })
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (located is null)
        {
            logger.LogInformation("Webhook PayPal {EventId} ignorado.", notice.EventId);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(located.OrderId, cancellationToken))
        {
            return;
        }

        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == located.OrderId, cancellationToken);
        var payment = order.Payments.Single(item => item.Id == located.Id);
        if (payment.ExternalOperationId is null)
        {
            if (!payment.PayPalHoldMustStay())
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Webhook PayPal {EventId} sin intento de creación local.", notice.EventId);
                return;
            }

            order.RememberPayPalOrder(payment.Id, notice.PaypalOrderId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            await using var continued = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockOrderAsync(located.OrderId, cancellationToken))
            {
                return;
            }

            order = await dbContext.Orders
                .Include(item => item.Payments)
                .ThenInclude(item => item.Events)
                .SingleAsync(item => item.Id == located.OrderId, cancellationToken);
            payment = order.Payments.Single(item => item.Id == located.Id);
            await ApplyNoticeAsync(order, payment, notice, continued, cancellationToken);
            return;
        }

        if (payment.Events.Any(item => item.ExternalEventId == notice.EventId && item.ProcessedAt is not null))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Webhook PayPal {EventId} duplicado.", notice.EventId);
            return;
        }

        var captureApproved = string.Equals(notice.EventType, "CHECKOUT.ORDER.APPROVED", StringComparison.Ordinal)
            && order.Status == OrderStatus.PendingPayment
            && payment.Status == PaymentStatus.Pending;
        var snapshot = await CurrentAsync(order, payment, captureApproved, cancellationToken);
        var reading = PayPalProtocol.Interpret(snapshot, order.Id, order.Number, order.TotalCents);
        await ApplyAsync(order, payment, reading, notice.EventId, notice.EventType, DateTimeOffset.UtcNow, observe: true, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEvent(exception))
        {
            logger.LogInformation("Webhook PayPal {EventId} duplicado.", notice.EventId);
        }
    }

    private async Task ApplyNoticeAsync(
        Order order,
        Payment payment,
        PayPalEventNotice notice,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (payment.Events.Any(item => item.ExternalEventId == notice.EventId && item.ProcessedAt is not null))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Webhook PayPal {EventId} duplicado.", notice.EventId);
            return;
        }

        var captureApproved = string.Equals(notice.EventType, "CHECKOUT.ORDER.APPROVED", StringComparison.Ordinal)
            && order.Status == OrderStatus.PendingPayment
            && payment.Status == PaymentStatus.Pending;
        var snapshot = await CurrentAsync(order, payment, captureApproved, cancellationToken);
        var reading = PayPalProtocol.Interpret(snapshot, order.Id, order.Number, order.TotalCents);
        await ApplyAsync(order, payment, reading, notice.EventId, notice.EventType, DateTimeOffset.UtcNow, observe: true, cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEvent(exception))
        {
            logger.LogInformation("Webhook PayPal {EventId} duplicado.", notice.EventId);
        }
    }

    public async Task<PayPalObservation> ObserveAsync(
        Guid orderId,
        string auditEventId,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsOperational || orderId == Guid.Empty || string.IsNullOrWhiteSpace(auditEventId))
        {
            return new PayPalObservation(PaymentEvidence.Unknown, PayPalRules.UnknownStatusMessage, false);
        }

        dbContext.ChangeTracker.Clear();
        var located = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.OrderId == orderId && payment.Provider == PaymentProvider.PayPal && payment.ExternalOperationId != null)
            .Select(payment => new { payment.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null)
        {
            return new PayPalObservation(PaymentEvidence.Unknown, PayPalRules.IdentityMessage, false);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(orderId, cancellationToken))
        {
            return new PayPalObservation(PaymentEvidence.Unknown, PayPalRules.IdentityMessage, false);
        }

        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        var payment = order.Payments.Single(item => item.Id == located.Id);
        PayPalOrderSnapshot snapshot;
        try
        {
            snapshot = await paypal.GetAsync(payment.ExternalOperationId!, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var reading = PayPalProtocol.Interpret(snapshot, order.Id, order.Number, order.TotalCents);
        var auditId = auditEventId.Trim();
        var eventId = reading.Evidence == PaymentEvidence.Paid && !string.IsNullOrWhiteSpace(reading.CaptureId)
            ? reading.CaptureId
            : reading.Evidence == PaymentEvidence.ClosedUnpaid
                ? "paypal-closed:" + payment.ExternalOperationId
                : auditId;
        var eventType = auditId.StartsWith("admin-paypal-recheck:", StringComparison.Ordinal)
            ? ProviderReconciliationRules.AdminEventType
            : ProviderReconciliationRules.PayPalEventType;
        var applied = await ApplyAsync(
            order,
            payment,
            reading,
            eventId,
            eventType,
            DateTimeOffset.UtcNow,
            observe: true,
            cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEvent(exception))
        {
            logger.LogInformation("Lectura PayPal del pedido {OrderId} duplicada.", orderId);
            return new PayPalObservation(reading.Evidence, applied.Attention, false);
        }

        return new PayPalObservation(reading.Evidence, applied.Attention ?? reading.Attention, applied.StockChanged);
    }

    private async Task<PayPalOrderSnapshot> CurrentAsync(
        Order order,
        Payment payment,
        bool captureIfApproved,
        CancellationToken cancellationToken)
    {
        var snapshot = await paypal.GetAsync(payment.ExternalOperationId!, cancellationToken);
        var reading = PayPalProtocol.Interpret(snapshot, order.Id, order.Number, order.TotalCents);
        if (!captureIfApproved || !reading.CanCapture)
        {
            return snapshot;
        }

        try
        {
            return await paypal.CaptureAsync(payment.ExternalOperationId!, PayPalProtocol.CaptureRequestId(payment.Id), cancellationToken);
        }
        catch (PayPalAlreadyCapturedException)
        {
            return await paypal.GetAsync(payment.ExternalOperationId!, cancellationToken);
        }
    }

    private async Task<PayPalApplication> ApplyAsync(
        Order order,
        Payment payment,
        PayPalReading reading,
        string eventId,
        string eventType,
        DateTimeOffset now,
        bool observe,
        CancellationToken cancellationToken)
    {
        if (payment.Events.Any(item => item.ExternalEventId == eventId && item.ProcessedAt is not null))
        {
            return new PayPalApplication(null, false);
        }

        var quiet = reading.Evidence == PaymentEvidence.StillPayable && reading.Attention is null && order.Status != OrderStatus.Paid;
        if (quiet && !observe)
        {
            return new PayPalApplication(null, false);
        }

        var reservation = await dbContext.StockReservations.SingleOrDefaultAsync(item => item.OrderId == order.Id, cancellationToken);
        string? attention = reading.Attention;
        var stockChanged = false;
        if (quiet)
        {
            attention = null;
        }
        else if (order.Status == OrderStatus.Paid)
        {
            attention = reading.Evidence == PaymentEvidence.Paid ? null : reading.Attention ?? PayPalRules.UnknownStatusMessage;
        }
        else if (reading.Evidence == PaymentEvidence.Paid && attention is null)
        {
            if (payment.Status != PaymentStatus.Pending
                || order.Status != OrderStatus.PendingPayment
                || reservation?.Status is not (StockReservationStatus.Reserved or StockReservationStatus.Confirming)
                || string.IsNullOrWhiteSpace(reading.CaptureId))
            {
                attention = PayPalRules.StockMessage;
                await incidents.EnsurePaidUnsettledAsync(
                    order.Id,
                    PaymentProvider.PayPal,
                    payment.ExternalOperationId,
                    now,
                    cancellationToken);
            }
            else
            {
                order.AssignCapture(payment.Id, reading.CaptureId);
                await new CheckoutSettlement(orderStock).ConfirmAuthorizedAsync(order, payment, reservation, now, cancellationToken);
                await incidents.ResolveVerifiedAsync(order.Id, now, cancellationToken);
                stockChanged = true;
            }
        }
        else if (reading.Evidence == PaymentEvidence.ClosedUnpaid
            && attention is null
            && payment.Status == PaymentStatus.Pending
            && order.Status == OrderStatus.PendingPayment)
        {
            if (payment.ExternalOperationId is not null)
            {
                order.AcceptVerifiedPayPalClosure(payment.Id);
            }

            if (payment.BlocksAutomaticRelease())
            {
                attention = ExternalHoldRules.UncertainHoldMessage;
                await incidents.EnsureHeldAsync(order.Id, PaymentProvider.PayPal, payment.ExternalOperationId, now, cancellationToken);
            }
            else
            {
                order.RecordPaymentFailure(payment.Id, now);
                if (reservation?.Status == StockReservationStatus.Reserved)
                {
                    await orderStock.ReleaseAsync(order.Id, now, cancellationToken);
                }

                order.Cancel(now);
                await incidents.ResolveVerifiedAsync(order.Id, now, cancellationToken);
                stockChanged = true;
            }
        }

        if (payment.Events.Any(item => item.ExternalEventId == eventId))
        {
            return new PayPalApplication(attention, stockChanged);
        }

        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), eventId, Fit(eventType), now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, attention);
        return new PayPalApplication(attention, stockChanged);
    }

    private static string EventId(PayPalReading reading, Payment payment)
    {
        if (reading.Evidence == PaymentEvidence.Paid && !string.IsNullOrWhiteSpace(reading.CaptureId))
        {
            return reading.CaptureId;
        }

        if (reading.Evidence == PaymentEvidence.ClosedUnpaid)
        {
            return "paypal-closed:" + payment.ExternalOperationId;
        }

        return "paypal-attention:" + payment.ExternalOperationId;
    }

    private static string Fit(string eventType)
    {
        var value = string.IsNullOrWhiteSpace(eventType) ? "paypal.event" : eventType.Trim();
        return value.Length <= PaymentEvent.EventTypeMaxLength ? value : value[..PaymentEvent.EventTypeMaxLength];
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

    private sealed record PayPalApplication(string? Attention, bool StockChanged);

    private static bool IsUniqueEvent(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return true;
            }
        }

        return false;
    }
}
