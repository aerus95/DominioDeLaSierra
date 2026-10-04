namespace DominioDeLaSierra.Domain.Entities;

public sealed class Wine
{
    public Guid ProductId { get; private set; }
    public string? Vintage { get; private set; }
    public string? Grape { get; private set; }
    public decimal? AlcoholPercent { get; private set; }

    public Product Product { get; private set; } = null!;

    private Wine()
    {
    }

    public Wine(Guid productId, string? vintage, string? grape, decimal? alcoholPercent)
    {
        ProductId = productId;
        Vintage = vintage;
        Grape = grape;
        AlcoholPercent = alcoholPercent;
    }

    public void Update(string? vintage, string? grape, decimal? alcoholPercent)
    {
        Vintage = vintage;
        Grape = grape;
        AlcoholPercent = alcoholPercent;
    }
}
