namespace DominioDeLaSierra.Application.Products.GetProducts;

public sealed record GetPackComponentsQuery(string Slug)
{
    public static GetPackComponentsQuery Create(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("slug is required.", nameof(slug));
        }

        return new GetPackComponentsQuery(slug.Trim());
    }
}
