using System.Globalization;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Api.Pages.Admin.Orders;

public static class OrderPresentation
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public static string Money(long cents)
    {
        return (cents / 100m).ToString("0.00", Spanish) + " €";
    }

    public static string When(DateTimeOffset value)
    {
        return AdminOrderClock.Convert(value).ToString("dd/MM/yyyy HH:mm", Spanish);
    }

    public static string Day(DateTimeOffset value)
    {
        return AdminOrderClock.Convert(value).ToString("dd/MM/yyyy", Spanish);
    }

    public static string Status(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.PendingPayment => "Pendiente de pago",
            OrderStatus.Paid => "Pagado",
            OrderStatus.Cancelled => "Cancelado",
            OrderStatus.Expired => "Caducado",
            _ => "Desconocido"
        };
    }

    public static string Fulfillment(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Unfulfilled => "Sin preparar",
            FulfillmentStatus.Preparing => "En preparación",
            FulfillmentStatus.Prepared => "Preparado",
            FulfillmentStatus.Shipped => "Enviado",
            _ => "Desconocido"
        };
    }

    public static string Payment(PaymentStatus status)
    {
        return status switch
        {
            PaymentStatus.Pending => "Pendiente",
            PaymentStatus.Succeeded => "Cobrado",
            PaymentStatus.Failed => "Fallido",
            PaymentStatus.Cancelled => "Cancelado",
            PaymentStatus.Refunded => "Reembolsado",
            _ => "Desconocido"
        };
    }

    public static string Reservation(StockReservationStatus status)
    {
        return status switch
        {
            StockReservationStatus.Reserved => "Reservado",
            StockReservationStatus.Confirming => "Confirmando",
            StockReservationStatus.Confirmed => "Confirmado",
            StockReservationStatus.Released => "Liberado",
            _ => "Desconocido"
        };
    }

    public static string StatusClass(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Paid => "paid",
            OrderStatus.PendingPayment => "pending",
            _ => "closed"
        };
    }

    public static string FulfillmentClass(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Preparing => "preparing",
            FulfillmentStatus.Prepared => "prepared",
            FulfillmentStatus.Shipped => "shipped",
            _ => "unfulfilled"
        };
    }

    public static FulfillmentStatus? Next(FulfillmentStatus status)
    {
        return status switch
        {
            FulfillmentStatus.Unfulfilled => FulfillmentStatus.Preparing,
            FulfillmentStatus.Preparing => FulfillmentStatus.Prepared,
            FulfillmentStatus.Prepared => FulfillmentStatus.Shipped,
            _ => null
        };
    }
}
