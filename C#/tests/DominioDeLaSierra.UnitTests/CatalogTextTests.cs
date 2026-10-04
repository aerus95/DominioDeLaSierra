using DominioDeLaSierra.Application.Common;

namespace DominioDeLaSierra.UnitTests;

public class CatalogTextTests
{
    [Fact]
    public void Accepts_a_normal_description()
    {
        var text = CatalogText.NormalizeOptionalText(
            "Notas de cata del vino.",
            CatalogLimits.ProductDescriptionMaxLength,
            allowLineBreaks: true,
            LengthMessage,
            CharactersMessage);

        Assert.Equal("Notas de cata del vino.", text);
    }

    [Fact]
    public void Accepts_a_description_of_300_characters()
    {
        var text = CatalogText.NormalizeOptionalText(
            new string('a', 300),
            CatalogLimits.ProductDescriptionMaxLength,
            allowLineBreaks: true,
            LengthMessage,
            CharactersMessage);

        Assert.Equal(300, text.Length);
    }

    [Fact]
    public void Rejects_a_description_of_301_characters()
    {
        var exception = Assert.Throws<ArgumentException>(() => CatalogText.NormalizeOptionalText(
            new string('a', 301),
            CatalogLimits.ProductDescriptionMaxLength,
            allowLineBreaks: true,
            LengthMessage,
            CharactersMessage));

        Assert.Equal(LengthMessage, exception.Message);
    }

    [Fact]
    public void Rejects_control_characters_that_are_not_line_breaks()
    {
        var exception = Assert.Throws<ArgumentException>(() => CatalogText.NormalizeOptionalText(
            "nota\u0007",
            CatalogLimits.ProductDescriptionMaxLength,
            allowLineBreaks: true,
            LengthMessage,
            CharactersMessage));

        Assert.Equal(CharactersMessage, exception.Message);
    }

    private const string LengthMessage = "La descripción no puede superar 300 caracteres.";
    private const string CharactersMessage = "La descripción contiene caracteres no permitidos.";
}
