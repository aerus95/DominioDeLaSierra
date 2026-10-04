using DominioDeLaSierra.Application.Common;

namespace DominioDeLaSierra.UnitTests;

public class CatalogWineTests
{
    [Fact]
    public void Accepts_a_vintage_and_its_20_character_limit()
    {
        Assert.Equal("2024", CatalogWine.NormalizeVintage("2024"));
        Assert.Equal(new string('a', 20), CatalogWine.NormalizeVintage(new string('a', 20)));
    }

    [Fact]
    public void Rejects_a_vintage_above_20_characters()
    {
        var exception = Assert.Throws<ArgumentException>(() => CatalogWine.NormalizeVintage(new string('a', 21)));

        Assert.Equal("La añada no puede superar 20 caracteres.", exception.Message);
    }

    [Fact]
    public void Accepts_a_grape_and_its_150_character_limit()
    {
        Assert.Equal("Rufete", CatalogWine.NormalizeGrape("Rufete"));
        Assert.Equal(new string('u', 150), CatalogWine.NormalizeGrape(new string('u', 150)));
    }

    [Fact]
    public void Rejects_a_grape_above_150_characters()
    {
        var exception = Assert.Throws<ArgumentException>(() => CatalogWine.NormalizeGrape(new string('u', 151)));

        Assert.Equal("La uva no puede superar 150 caracteres.", exception.Message);
    }
}
