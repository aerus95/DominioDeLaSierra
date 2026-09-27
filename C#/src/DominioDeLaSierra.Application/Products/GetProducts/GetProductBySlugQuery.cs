namespace DominioDeLaSierra.Application.Products.GetProducts;

public sealed record GetProductBySlugQuery(string Slug)
{
    public static GetProductBySlugQuery Create(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("slug is required.", nameof(slug));
        }

        return new GetProductBySlugQuery(slug.Trim());
    }
}
