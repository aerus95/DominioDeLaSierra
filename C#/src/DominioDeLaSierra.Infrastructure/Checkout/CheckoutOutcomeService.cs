using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Checkout;

/// <summary>
/// Lee el estado local de un pedido de invitado. No llama a Stripe ni cambia el pedido.
/// </summary>
public sealed class CheckoutOutcomeService(ApplicationDbContext dbContext) : ICheckoutOutcome
{
    public async Task<CheckoutOutcomeDto> ReadAsync(
        Guid orderId,
        string? checkoutAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty || string.IsNullOrWhiteSpace(checkoutAccessToken))
        {
            throw new CheckoutNotFoundException();
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

        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == orderId)
            .Select(item => new
            {
                item.CheckoutAccessTokenHash,
                item.Number,
                item.Status,
                item.TotalCents,
                item.Currency
            })
            .SingleOrDefaultAsync(cancellationToken);
        var storedHash = order?.CheckoutAccessTokenHash ?? new string('0', CheckoutAccessToken.HashLength);
        if (order is null || !CheckoutAccessToken.FixedEquals(storedHash, hash))
        {
            throw new CheckoutNotFoundException();
        }

        return CheckoutOutcomeRules.Present(order.Number, order.Status, order.TotalCents, order.Currency);
    }
}
