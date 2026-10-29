using System.Data;
using System.Globalization;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Prepara la redirección de pruebas. El importe sale del pedido guardado, en céntimos y sin redondeo.
/// CheckoutExpiresAt es la reserva de stock ya creada, no una caducidad publicada por Redsys.
/// </summary>
public sealed class RedsysPaymentStarter(
    ApplicationDbContext dbContext,
    IRedsysOperationNumbers operationNumbers,
    RedsysSettings settings,
    RedsysRedirectFailureProbe redirectFailures)
{
    public async Task<CheckoutPaymentDto> StartAsync(GuestOrderDto order, CancellationToken cancellationToken)
    {
        if (!settings.IsOperational)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }

        if (!string.Equals(order.Currency, OrderAmounts.Currency, StringComparison.Ordinal)
            || order.TotalCents <= 0
            || order.TotalCents > 999_999_999_999)
        {
            throw new InvalidOperationException(CheckoutLimits.PaymentUnavailableMessage);
        }

        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockOrderAsync(order.OrderId, cancellationToken))
        {
            throw new CheckoutNotFoundException();
        }

        var stored = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.Id == order.OrderId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        EnsurePayable(stored, now);
        if (stored.TotalCents != order.TotalCents || !string.Equals(stored.Currency, order.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(RedsysNotificationRules.AmountMessage);
        }

        var pending = stored.Payments.SingleOrDefault(payment => payment.Status == PaymentStatus.Pending);
        if (pending is not null && pending.Provider != PaymentProvider.Redsys)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }

        if (pending is null)
        {
            pending = stored.AddPayment(Guid.NewGuid(), now, PaymentProvider.Redsys);
        }

        var operation = pending.RedsysOrderNumber ?? await operationNumbers.NextAsync(cancellationToken);
        if (pending.RedsysOrderNumber is null)
        {
            stored.AssignExternalCheckout(
                pending.Id,
                operation,
                RedsysSignature.TestEndpoint,
                stored.ReservationExpiresAt,
                operation);
        }

        var fields = RedirectFields(stored.TotalCents, operation);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (redirectFailures.ConsumeFailure())
        {
            throw new InvalidOperationException(RedsysRedirectFailureProbe.NotEmittedMessage);
        }

        await transaction.CommitAsync(cancellationToken);
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
            RedsysSignature.TestEndpoint,
            stored.ReservationExpiresAt,
            order.CheckoutAccessToken,
            nameof(PaymentProvider.Redsys),
            "POST",
            fields);
    }

    private Dictionary<string, string> RedirectFields(long amountCents, string operation)
    {
        var parameters = RedsysSignature.EncodeParameters(new Dictionary<string, string>
        {
            ["DS_MERCHANT_ORDER"] = operation,
            ["DS_MERCHANT_MERCHANTCODE"] = settings.MerchantCode,
            ["DS_MERCHANT_TERMINAL"] = settings.Terminal,
            ["DS_MERCHANT_CURRENCY"] = RedsysSignature.EuroCurrency,
            ["DS_MERCHANT_TRANSACTIONTYPE"] = RedsysSignature.AuthorizationType,
            ["DS_MERCHANT_AMOUNT"] = amountCents.ToString(CultureInfo.InvariantCulture),
            ["DS_MERCHANT_MERCHANTURL"] = settings.MerchantUrl,
            ["DS_MERCHANT_URLOK"] = settings.UrlOk,
            ["DS_MERCHANT_URLKO"] = settings.UrlKo
        });
        return new Dictionary<string, string>
        {
            ["Ds_SignatureVersion"] = RedsysSignature.Version,
            ["Ds_MerchantParameters"] = parameters,
            ["Ds_Signature"] = RedsysSignature.Sign(parameters, operation, settings.SecretKey)
        };
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
}
