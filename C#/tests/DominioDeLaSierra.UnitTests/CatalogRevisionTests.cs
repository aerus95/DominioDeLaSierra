using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.UnitTests;

public class CatalogRevisionTests
{
    [Fact]
    public void Same_catalog_data_produces_the_same_revision()
    {
        Assert.Equal(Sample(), Sample());
    }

    [Fact]
    public void A_functional_change_produces_a_different_revision()
    {
        var original = Sample();
        var renamed = Compute(name: "Otro nombre");

        Assert.NotEqual(original, renamed);
    }

    [Fact]
    public void An_image_only_change_does_not_change_the_revision()
    {
        var beforeImageChange = Sample();
        var afterImageChange = Sample();

        Assert.Equal(beforeImageChange, afterImageChange);
        Assert.DoesNotContain("image", beforeImageChange, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sample() => Compute(name: "Tinto de prueba");

    private static string Compute(string name) =>
        CatalogRevision.Compute(
            ProductKind.Wine,
            "TEST-REV",
            name,
            "tinto-de-prueba",
            "Descripción",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            18.90m,
            21m,
            true,
            "2024",
            "Rufete",
            13.50m,
            []);
}
