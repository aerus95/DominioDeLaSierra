using System.Data;
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Checkout;

public sealed class OrderPaymentSessionService(
    ApplicationDbContext dbContext,
    IStripeCheckoutGateway gateway,
    IConfiguration configuration,
    ICheckout checkout,
    IOrderStock orderStock) : IOrderPaymentSessions
{
    public async Task<CheckoutPaymentDto> PayAsync(
        string? idempotencyKey,
        PlaceGuestOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var placement = await checkout.PlaceGuestOrderAsync(
            idempotencyKey,
            request,
            cancellationToken,
            StripeCheckoutExpiry.PaymentHold);
        if (!string.Equals(placement.Order.Status, nameof(OrderStatus.PendingPayment), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(CheckoutLimits.HoldReleasedMessage);
        }

        try
        {
            var session = await OpenAsync(placement.Order.OrderId, null, cancellationToken);
            return new CheckoutPaymentDto(
                placement.Order.OrderId,
                placement.Order.Number,
                placement.Order.ProductSubtotalCents,
                placement.Order.ShippingCents,
                placement.Order.ShippingVatRate,
                placement.Order.TotalCents,
                placement.Order.Currency,
                placement.Order.ReservationExpiresAt,
                placement.Order.Status,
                placement.Order.ShippingVatPending,
                session.CheckoutUrl,
                session.ExpiresAt,
                placement.Order.CheckoutAccessToken);
        }
        catch (InvalidOperationException exception) when (RequiresRelease(exception))
        {
            await ReleaseUnpayableHoldAsync(placement.Order.OrderId, cancellationToken);
            throw new InvalidOperationException(CheckoutLimits.HoldReleasedMessage);
        }
    }

    public async Task<CheckoutSessionDto> StartAsync(
        Guid orderId,
        string? checkoutAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty || string.IsNullOrWhiteSpace(checkoutAccessToken))
        {
            throw new ArgumentException(CheckoutLimits.CheckoutAccessMessage);
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

        return await OpenAsync(orderId, hash, cancellationToken);
    }

    private async Task<CheckoutSessionDto> OpenAsync(Guid orderId, string? accessHash, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var urls = RequireUrls();
        var now = DateTimeOffset.UtcNow;
        Guid paymentId;
        StripeCheckoutDraft draft;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            if (!await LockOrderAsync(orderId, cancellationToken))
            {
                throw new CheckoutNotFoundException();
            }

            var order = await dbContext.Orders
                .Include(item => item.Items)
                .Include(item => item.Payments)
                .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
            if (order is null
                || (accessHash is not null && !CheckoutAccessToken.FixedEquals(order.CheckoutAccessTokenHash, accessHash)))
            {
                throw new CheckoutNotFoundException();
            }

            EnsurePayable(order, now);
            var reservation = await dbContext.StockReservations
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.OrderId == order.Id, cancellationToken);
            if (reservation is null || reservation.Status != StockReservationStatus.Reserved)
            {
                throw new InvalidOperationException(OrderStockRules.MissingReservationMessage);
            }

            var pending = order.Payments.SingleOrDefault(payment => payment.Status == PaymentStatus.Pending);
            if (pending?.CheckoutUrl is not null && pending.CheckoutExpiresAt > now)
            {
                var stored = new CheckoutSessionDto(pending.CheckoutUrl, pending.CheckoutExpiresAt.Value);
                await transaction.CommitAsync(cancellationToken);
                return stored;
            }

            var expiresAt = StripeCheckoutExpiry.PaymentSessionExpiry(now, order.ReservationExpiresAt);

            if (pending is null)
            {
                pending = order.AddPayment(Guid.NewGuid(), now);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            paymentId = pending.Id;
            draft = new StripeCheckoutDraft(
                order.Id,
                order.Number,
                order.TotalCents,
                StripeCheckoutLines.For(order),
                expiresAt,
                urls.SuccessUrl,
                urls.CancelUrl);
            await transaction.CommitAsync(cancellationToken);
        }

        dbContext.ChangeTracker.Clear();

        CreatedStripeCheckout created;
        try
        {
            created = await gateway.CreateAsync(draft, $"checkout-session:{paymentId:N}", cancellationToken);
        }
        catch (StripeCheckoutException)
        {
            await FailPaymentAsync(orderId, paymentId, cancellationToken);
            throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
        }

        try
        {
            return await PersistSessionAsync(orderId, paymentId, created, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsConstraint(exception, "IX_Payments_StripeCheckoutSessionId"))
        {
            dbContext.ChangeTracker.Clear();
            var stored = await FindStoredSessionAsync(orderId, accessHash, cancellationToken);
            if (stored is null)
            {
                throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
            }

            return stored;
        }
    }

    private async Task<CheckoutSessionDto> PersistSessionAsync(
        Guid orderId,
        Guid paymentId,
        CreatedStripeCheckout created,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        if (created.AmountCents != order.TotalCents
            || created.ExpiresAt > order.ReservationExpiresAt.Subtract(StripeCheckoutExpiry.ReservationTail)
            || !created.SessionId.StartsWith("cs_test_", StringComparison.Ordinal))
        {
            order.RecordPaymentFailure(paymentId, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
        }

        var pending = order.Payments.Single(payment => payment.Id == paymentId);
        if (pending.CheckoutUrl is not null && pending.StripeCheckoutSessionId == created.SessionId)
        {
            return new CheckoutSessionDto(pending.CheckoutUrl, pending.CheckoutExpiresAt!.Value);
        }

        order.AssignHostedCheckout(paymentId, created.SessionId, created.Url, created.ExpiresAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CheckoutSessionDto(created.Url, created.ExpiresAt);
    }

    private async Task FailPaymentAsync(Guid orderId, Guid paymentId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleAsync(item => item.Id == orderId, cancellationToken);
        var payment = order.Payments.SingleOrDefault(item => item.Id == paymentId);
        if (payment is null || payment.Status != PaymentStatus.Pending || payment.StripeCheckoutSessionId is not null)
        {
            return;
        }

        order.RecordPaymentFailure(paymentId, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CheckoutSessionDto?> FindStoredSessionAsync(
        Guid orderId,
        string? accessHash,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null
            || (accessHash is not null && !CheckoutAccessToken.FixedEquals(order.CheckoutAccessTokenHash, accessHash)))
        {
            return null;
        }

        var pending = order.Payments.SingleOrDefault(payment =>
            payment.Status == PaymentStatus.Pending && payment.CheckoutUrl is not null);
        return pending is null
            ? null
            : new CheckoutSessionDto(pending.CheckoutUrl!, pending.CheckoutExpiresAt!.Value);
    }

    private (string SuccessUrl, string CancelUrl) RequireUrls()
    {
        var success = configuration["Stripe:SuccessUrl"];
        var cancel = configuration["Stripe:CancelUrl"];
        if (!IsLocalOrPublicUrl(success) || !IsLocalOrPublicUrl(cancel))
        {
            throw new StripeCheckoutException(CheckoutLimits.PaymentUnavailableMessage);
        }

        return (success!, cancel!);
    }

    private static bool IsLocalOrPublicUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var url)
            && url.Scheme is "https" or "http"
            && string.IsNullOrEmpty(url.UserInfo);
    }

    private async Task ReleaseUnpayableHoldAsync(Guid orderId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await orderStock.ReleaseAsync(orderId, now, cancellationToken);
        }
        catch (InvalidOperationException exception) when (
            exception.Message == OrderStockRules.MissingReservationMessage
            || exception.Message == OrderStockRules.ReservationClosedMessage)
        {
        }

        dbContext.ChangeTracker.Clear();
        var order = await dbContext.Orders
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null || order.Status != OrderStatus.PendingPayment)
        {
            return;
        }

        order.Cancel(now);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool RequiresRelease(InvalidOperationException exception)
    {
        return exception.Message == StripeCheckoutExpiry.OutsideStripeWindowMessage
            || exception.Message == StripeCheckoutExpiry.ExpiredMessage
            || exception.Message == OrderStockRules.MissingReservationMessage;
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

    private static bool IsConstraint(DbUpdateException exception, string constraintName)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgres.ConstraintName, constraintName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
