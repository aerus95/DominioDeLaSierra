namespace DominioDeLaSierra.Application.Common;

public static class CatalogWine
{
    public const string MissingProfileMessage = "La ficha de vino de este producto no está disponible.";

    public static string? NormalizeVintage(string? value) =>
        EmptyToNull(CatalogText.NormalizeOptionalText(
            value,
            CatalogLimits.WineVintageMaxLength,
            allowLineBreaks: false,
            "La añada no puede superar 20 caracteres.",
            "La añada contiene caracteres no permitidos."));

    public static string? NormalizeGrape(string? value) =>
        EmptyToNull(CatalogText.NormalizeOptionalText(
            value,
            CatalogLimits.WineGrapeMaxLength,
            allowLineBreaks: false,
            "La uva no puede superar 150 caracteres.",
            "La uva contiene caracteres no permitidos."));

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
