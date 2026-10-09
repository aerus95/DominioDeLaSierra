using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.UnitTests;

public class StockDemandTests
{
    [Fact]
    public void Sums_a_wine_and_a_pack_that_share_it_before_checking_stock()
    {
        var wineId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-DEMAND-1",
            "demand-1",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [
                new OrderLine(wineId, "Dominium", "DOM-1", ProductKind.Wine, 2, 1_890, 21m, null),
                new OrderLine(
                    Guid.NewGuid(),
                    "Caja",
                    "PACK-1",
                    ProductKind.Pack,
                    3,
                    5_400,
                    21m,
                    [
                        new OrderLineComponent(wineId, "Dominium", "DOM-1", 2),
                        new OrderLineComponent(otherId, "Blanco", "BLA-1", 1)
                    ])
            ],
            new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

        var demand = StockDemand.Collect(order.Items).ToDictionary(item => item.ProductId, item => item.Quantity);

        Assert.Equal(2, demand.Count);
        Assert.Equal(8, demand[wineId]);
        Assert.Equal(3, demand[otherId]);
    }

    [Fact]
    public void Calculates_how_many_packs_the_components_can_cover()
    {
        Assert.Equal(2, PackAvailability.SellableQuantity([(2, 5), (1, 2)]));
        Assert.Equal(0, PackAvailability.SellableQuantity([(2, 1)]));
    }

    [Fact]
    public void Rejects_an_aggregated_quantity_above_the_stock_limit()
    {
        var sharedId = Guid.NewGuid();
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-DEMAND-2",
            "demand-2",
            "Ana Rivas",
            "ana@example.com",
            "+34600111222",
            "Calle Mayor 1",
            "37001",
            "Salamanca",
            "Salamanca",
            null,
            [
                new OrderLine(
                    Guid.NewGuid(),
                    "Caja A",
                    "PACK-A",
                    ProductKind.Pack,
                    600_000,
                    1_000,
                    21m,
                    [new OrderLineComponent(sharedId, "Dominium", "DOM-1", 1)]),
                new OrderLine(
                    Guid.NewGuid(),
                    "Caja B",
                    "PACK-B",
                    ProductKind.Pack,
                    600_000,
                    1_000,
                    21m,
                    [new OrderLineComponent(sharedId, "Dominium", "DOM-1", 1)])
            ],
            new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

        var exception = Assert.Throws<ArgumentException>(() => StockDemand.Collect(order.Items));
        Assert.Equal(StockDemand.InvalidQuantityMessage, exception.Message);
    }
}
