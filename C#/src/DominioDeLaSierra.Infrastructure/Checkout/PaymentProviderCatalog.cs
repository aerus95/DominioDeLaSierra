using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Solo anuncia proveedores que el checkout puede iniciar.
/// Redsys aparece solo en TEST. PayPal aparece solo en Sandbox con cliente, secreto, webhook y retornos locales.
/// </summary>
public sealed class PaymentProviderCatalog(IConfiguration configuration) : IPaymentProviders
{
    public IReadOnlyList<PaymentProviderOfferDto> Configured()
    {
        var offers = new List<PaymentProviderOfferDto>();
        if (StripeConfigured())
        {
            offers.Add(new PaymentProviderOfferDto(nameof(PaymentProvider.Stripe), "Stripe"));
        }

        if (RedsysSettings.Read(configuration).IsOperational)
        {
            offers.Add(new PaymentProviderOfferDto(nameof(PaymentProvider.Redsys), "Redsys"));
        }

        if (PayPalSettings.Read(configuration).IsOperational)
        {
            offers.Add(new PaymentProviderOfferDto(nameof(PaymentProvider.PayPal), "PayPal"));
        }

        return offers;
    }

    public void EnsureSelectable(string? requested)
    {
        if (!PaymentProviderRules.TryParse(requested, out var provider))
        {
            throw new ArgumentException(string.IsNullOrWhiteSpace(requested)
                ? PaymentProviderRules.RequiredMessage
                : PaymentProviderRules.UnknownMessage);
        }

        var available = provider switch
        {
            PaymentProvider.Stripe => StripeConfigured(),
            PaymentProvider.Redsys => RedsysSettings.Read(configuration).IsOperational,
            PaymentProvider.PayPal => PayPalSettings.Read(configuration).IsOperational,
            _ => false
        };
        if (!available)
        {
            throw new InvalidOperationException(PaymentProviderRules.UnavailableMessage);
        }
    }

    private bool StripeConfigured()
    {
        if (string.Equals(configuration["Stripe:Gateway"], "Stub", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"])
            || !string.IsNullOrWhiteSpace(configuration["STRIPE_SECRET_KEY"]);
    }
}
