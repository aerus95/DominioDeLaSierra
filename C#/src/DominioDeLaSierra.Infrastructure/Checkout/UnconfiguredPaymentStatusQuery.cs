using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// No llama a Redsys ni a PayPal. Sin contrato ni credenciales, el estado es desconocido y la reserva se conserva.
/// </summary>
public sealed class UnconfiguredPaymentStatusQuery(PaymentProvider provider) : IPaymentStatusQuery
{
    public PaymentProvider Provider { get; } = provider;

    public bool IsConfigured => false;

    public Task<PaymentEvidence> InspectAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PaymentEvidence.Unknown);
    }
}
