namespace DominioDeLaSierra.Application.Inventory;

public static class OrderStockRules
{
    public const string MissingOrderMessage = "El pedido no existe.";
    public const string NotPendingMessage = "El pedido no está pendiente de pago.";
    public const string UnavailableProductMessage = "El producto ya no está disponible.";
    public const string InsufficientStockMessage = "No hay stock suficiente.";
    public const string ReservationExpiredMessage = "La reserva del pedido ha caducado.";
    public const string ReservationClosedMessage = "La reserva de este pedido ya no se puede aplicar.";
    public const string MissingReservationMessage = "El pedido no tiene una reserva de stock.";
    public const string PaidReleaseMessage = "No se puede liberar el stock de un pedido pagado.";
    public const string ConfirmingReleaseMessage = "No se puede liberar el stock mientras se confirma el pago.";
    public const string SaleNote = "Reserva de pedido";
    public const string ReleaseNote = "Liberación de reserva";
}

public interface IOrderStock
{
    Task ReserveAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task ReleaseAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task BeginPaymentConfirmationAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default);

    Task ConfirmPaymentAsync(Guid orderId, DateTimeOffset at, CancellationToken cancellationToken = default);
}
