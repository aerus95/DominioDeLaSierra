using System.Data;
using System.Security.Cryptography;
using System.Text;
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
/// La notificación firmada es la única confirmación. El retorno del navegador no entra aquí.
/// Una denegación firmada y coincidente cierra ese intento y devuelve el stock.
/// Un resultado no numérico no libera la reserva: Redsys no ofrece, en esta modalidad, una consulta que lo autorice.
/// </summary>
public sealed class RedsysNotificationService(
    ApplicationDbContext dbContext,
    IOrderStock orderStock,
    RedsysSettings settings,
    RedsysNotificationFailureProbe failures,
    ReservationHoldIncidentRecorder incidents,
    ILogger<RedsysNotificationService> logger) : IRedsysNotifications
{
    public async Task<string> ReceiveAsync(
        string? signatureVersion,
        string? merchantParameters,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsOperational
            || !string.Equals(signatureVersion, RedsysSignature.Version, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(merchantParameters))
        {
            throw new RedsysSignatureException(RedsysSignature.InvalidSignatureMessage);
        }

        using var document = RedsysSignature.DecodeParameters(merchantParameters);
        var notice = RedsysParameterReader.Read(document.RootElement);
        if (notice.Order.Length is < 4 or > 12
            || !RedsysSignature.Matches(merchantParameters, notice.Order, settings.SecretKey, signature))
        {
            throw new RedsysSignatureException(RedsysSignature.InvalidSignatureMessage);
        }

        dbContext.ChangeTracker.Clear();
        var located = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Provider == PaymentProvider.Redsys && payment.RedsysOrderNumber == notice.Order)
            .Select(payment => new { payment.OrderId, payment.Id })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null)
        {
            logger.LogInformation("Notificación Redsys {Operation} ignorada.", notice.Order);
            return RedsysNotificationRules.Ignored;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(located.OrderId, cancellationToken))
        {
            return RedsysNotificationRules.Ignored;
        }

        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .ThenInclude(payment => payment.Events)
            .SingleAsync(item => item.Id == located.OrderId, cancellationToken);
        var payment = order.Payments.Single(item => item.Id == located.Id);
        var eventId = EventId(merchantParameters, notice);
        if (payment.Events.Any(item => item.ExternalEventId == eventId && item.ProcessedAt is not null))
        {
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Notificación Redsys {Operation} duplicada.", notice.Order);
            return RedsysNotificationRules.Duplicate;
        }

        var reservation = await dbContext.StockReservations
            .SingleOrDefaultAsync(item => item.OrderId == order.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var outcome = Classify(notice, order, payment);
        var attention = Attention(notice, order, payment, reservation, outcome);
        var disposition = RedsysNotificationRules.Ignored;
        if (attention is null && outcome == RedsysOutcome.Authorized && order.Status != OrderStatus.Paid)
        {
            await new CheckoutSettlement(orderStock).ConfirmAuthorizedAsync(
                order,
                payment,
                reservation!,
                now,
                cancellationToken);
            await incidents.ResolveVerifiedAsync(order.Id, now, cancellationToken);
            disposition = RedsysNotificationRules.Confirmed;
        }
        else if (attention is null && outcome == RedsysOutcome.Denied && payment.Status == PaymentStatus.Pending)
        {
            if (order.Payments.Any(item => item.BlocksAutomaticRelease()))
            {
                attention = ExternalHoldRules.UncertainHoldMessage;
                disposition = RedsysNotificationRules.NeedsReconciliation;
                await incidents.EnsureHeldAsync(
                    order.Id,
                    PaymentProvider.Redsys,
                    payment.RedsysOrderNumber ?? payment.ExternalOperationId,
                    now,
                    cancellationToken);
            }
            else
            {
                order.RecordPaymentFailure(payment.Id, now);
                if (reservation?.Status == StockReservationStatus.Reserved)
                {
                    await orderStock.ReleaseAsync(order.Id, now, cancellationToken);
                }

                if (reservation?.Status != StockReservationStatus.Confirming && order.Status == OrderStatus.PendingPayment)
                {
                    order.Cancel(now);
                }

                await incidents.ResolveVerifiedAsync(order.Id, now, cancellationToken);
                disposition = RedsysNotificationRules.Denied;
            }
        }
        else if (attention is not null)
        {
            disposition = RedsysNotificationRules.NeedsReconciliation;
            if (outcome == RedsysOutcome.Authorized
                && attention is RedsysNotificationRules.StockMessage or RedsysNotificationRules.UnknownMessage)
            {
                await incidents.EnsurePaidUnsettledAsync(
                    order.Id,
                    PaymentProvider.Redsys,
                    payment.RedsysOrderNumber ?? payment.ExternalOperationId,
                    now,
                    cancellationToken);
            }
        }

        var recorded = order.RecordPaymentEvent(payment.Id, Guid.NewGuid(), eventId, EventType(notice), now);
        order.MarkPaymentEventProcessed(payment.Id, recorded.ExternalEventId, now, attention);
        if (failures.ConsumeFailure())
        {
            throw new IOException(RedsysNotificationRules.UnavailableMessage);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueEvent(exception))
        {
            logger.LogInformation("Notificación Redsys {Operation} duplicada.", notice.Order);
            return RedsysNotificationRules.Duplicate;
        }

        logger.LogInformation("Notificación Redsys {Operation} {Disposition}.", notice.Order, disposition);
        return disposition;
    }

    private string? Attention(
        RedsysMerchantParameters notice,
        Order order,
        Payment payment,
        StockReservation? reservation,
        RedsysOutcome outcome)
    {
        if (notice.AmountCents != order.TotalCents || notice.AmountCents != payment.AmountCents)
        {
            return RedsysNotificationRules.AmountMessage;
        }

        if (!string.Equals(notice.Currency, RedsysSignature.EuroCurrency, StringComparison.Ordinal))
        {
            return RedsysNotificationRules.CurrencyMessage;
        }

        if (!string.Equals(notice.MerchantCode, settings.MerchantCode, StringComparison.Ordinal)
            || !RedsysParameterReader.SameTerminal(settings.Terminal, notice.Terminal))
        {
            return RedsysNotificationRules.MerchantMessage;
        }

        if (notice.TransactionType is not null
            && !string.Equals(notice.TransactionType, RedsysSignature.AuthorizationType, StringComparison.Ordinal))
        {
            return RedsysNotificationRules.UnknownMessage;
        }

        if (outcome == RedsysOutcome.Unknown)
        {
            return RedsysNotificationRules.UnknownMessage;
        }

        if (outcome != RedsysOutcome.Authorized)
        {
            return null;
        }

        if (order.Status == OrderStatus.Paid)
        {
            return null;
        }

        if (reservation?.Status is not (StockReservationStatus.Reserved or StockReservationStatus.Confirming))
        {
            return RedsysNotificationRules.StockMessage;
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Expired || payment.Status != PaymentStatus.Pending)
        {
            return RedsysNotificationRules.UnknownMessage;
        }

        return null;
    }

    private static RedsysOutcome Classify(RedsysMerchantParameters notice, Order order, Payment payment)
    {
        if (notice.AmountCents != order.TotalCents || notice.AmountCents != payment.AmountCents)
        {
            return RedsysOutcome.Unknown;
        }

        return RedsysResponse.Classify(notice.Response);
    }

    private static string EventId(string merchantParameters, RedsysMerchantParameters notice)
    {
        var response = string.IsNullOrWhiteSpace(notice.Response) ? "none" : notice.Response.Trim();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(merchantParameters)))[..16];
        var value = $"redsys:{notice.Order}:{response}:{hash}";
        return value.Length <= 255 ? value : value[..255];
    }

    private static string EventType(RedsysMerchantParameters notice)
    {
        var kind = RedsysResponse.Classify(notice.Response);
        var name = kind switch
        {
            RedsysOutcome.Authorized => "redsys.authorized",
            RedsysOutcome.Denied => "redsys.denied",
            _ => "redsys.unknown"
        };
        if (notice.AuthorisationCode is null)
        {
            return name;
        }

        var withCode = name + "." + notice.AuthorisationCode;
        return withCode.Length <= 120 ? withCode : name;
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

    private static bool IsUniqueEvent(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgres.ConstraintName, "IX_PaymentEvents_ExternalEventId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
