using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public sealed record PaymentProviderOfferDto(string Code, string DisplayName);

public interface IPaymentProviders
{
    IReadOnlyList<PaymentProviderOfferDto> Configured();

    void EnsureSelectable(string? requested);
}

/// <summary>
/// Consulta de un proveedor que no es Stripe. Una respuesta desconocida o un proveedor sin configurar
/// no autoriza a liberar stock. Stripe conserva su propia consulta.
/// </summary>
public interface IPaymentStatusQuery
{
    PaymentProvider Provider { get; }

    bool IsConfigured { get; }

    Task<PaymentEvidence> InspectAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reserva el siguiente DS_MERCHANT_ORDER numérico. No lo usa el checkout mientras Redsys no esté configurado.
/// </summary>
public interface IRedsysOperationNumbers
{
    Task<string> NextAsync(CancellationToken cancellationToken = default);
}
