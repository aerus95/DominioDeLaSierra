using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.Domain;

/// <summary>
/// Líneas que se cobran en Stripe. El importe sale del pedido persistido.
/// El envío entra como bruto de 8,00 € o no entra. No se envía un tipo impositivo:
/// el IVA del transporte sigue pendiente y no se asume un 21 %.
/// </summary>
public static class StripeCheckoutLines
{
    public const string ShippingName = "Envío peninsular";

    public sealed record Line(string Name, int Quantity, long UnitAmountCents);

    public static IReadOnlyList<Line> For(Order order)
    {
        var lines = new List<Line>();
        long sum = 0;
        foreach (var item in order.Items)
        {
            lines.Add(new Line(item.Name, item.Quantity, item.UnitPriceCents));
            sum = checked(sum + OrderAmounts.Multiply(item.UnitPriceCents, item.Quantity));
        }

        if (order.ShippingCents > 0)
        {
            lines.Add(new Line(ShippingName, 1, order.ShippingCents));
            sum = checked(sum + order.ShippingCents);
        }

        if (sum != order.TotalCents)
        {
            throw new InvalidOperationException("El importe del pago no coincide con el pedido.");
        }

        return lines;
    }
}
