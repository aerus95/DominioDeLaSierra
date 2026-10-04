namespace DominioDeLaSierra.Application.Common;

public static class CatalogLimits
{
    public const int CategoryNameMaxLength = 150;
    public const int CategorySlugMaxLength = 180;
    public const int ProductReferenceMaxLength = 80;
    public const int ProductNameMaxLength = 200;
    public const int ProductSlugMaxLength = 220;
    public const int ProductDescriptionMaxLength = 300;

    public const decimal MaxPrice = 9_999_999_999.99m;
    public const decimal MaxVatRate = 999.99m;
    public const int MoneyScale = 2;

    public const int WineVintageMaxLength = 20;
    public const int WineGrapeMaxLength = 150;
    public const decimal MaxAlcoholPercent = 99.99m;

    public const int ComponentReferenceMaxLength = 80;
}
