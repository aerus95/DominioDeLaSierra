namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Identificador de una petición de checkout. No contiene datos del pedido ni de la pasarela.
/// </summary>
public sealed class CheckoutCorrelation
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
}
