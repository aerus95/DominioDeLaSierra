using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Checkout;

public static class CheckoutOutcomeRules
{
    public static CheckoutOutcomeDto Present(string? number, OrderStatus status, long totalCents, string? currency)
    {
        if (string.IsNullOrWhiteSpace(number)
            || !Enum.IsDefined(status)
            || totalCents < 0
            || !string.Equals(currency, "EUR", StringComparison.Ordinal))
        {
            throw new CheckoutNotFoundException();
        }

        return new CheckoutOutcomeDto(number, status.ToString(), totalCents, "EUR");
    }
}
