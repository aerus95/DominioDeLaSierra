namespace DominioDeLaSierra.Application.Products.GetProducts;

public sealed record GetProductsQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Category,
    decimal? MinPrice,
    decimal? MaxPrice)
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;

    public static GetProductsQuery Create(
        int page = 1,
        int pageSize = DefaultPageSize,
        string? search = null,
        string? category = null,
        decimal? minPrice = null,
        decimal? maxPrice = null)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(page), "page must be greater than or equal to 1.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), $"pageSize must be between 1 and {MaxPageSize}.");
        }

        if (minPrice is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minPrice), "minPrice cannot be negative.");
        }

        if (maxPrice is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPrice), "maxPrice cannot be negative.");
        }

        if (minPrice is not null && maxPrice is not null && minPrice > maxPrice)
        {
            throw new ArgumentException("minPrice cannot be greater than maxPrice.");
        }

        return new GetProductsQuery(
            page,
            pageSize,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            minPrice,
            maxPrice);
    }
}
