using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class CatalogCompositionTests
{
    [Fact]
    public void Rejects_an_empty_pack()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CatalogComposition.EnsureDraft([]));

        Assert.Equal(CatalogComposition.EmptyPackMessage, exception.Message);
    }

    [Fact]
    public void Accepts_one_component_with_a_positive_quantity()
    {
        var component = (ProductId: Guid.NewGuid(), Quantity: 2);

        CatalogComposition.EnsureDraft([component]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_quantity_that_is_not_positive(int quantity)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CatalogComposition.EnsureDraft([(Guid.NewGuid(), quantity)]));

        Assert.Equal(CatalogComposition.InvalidQuantityMessage, exception.Message);
    }

    [Fact]
    public void Rejects_a_repeated_component()
    {
        var productId = Guid.NewGuid();
        var exception = Assert.Throws<ArgumentException>(() =>
            CatalogComposition.EnsureDraft([(productId, 1), (productId, 2)]));

        Assert.Equal(CatalogComposition.RepeatedComponentMessage, exception.Message);
    }

    [Fact]
    public void Rejects_a_pack_used_as_a_component()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CatalogComposition.RejectIneligible(ProductKind.Pack, "TEST-PACK"));

        Assert.Equal(CatalogComposition.PackComponentMessage("TEST-PACK"), exception.Message);
    }
}
