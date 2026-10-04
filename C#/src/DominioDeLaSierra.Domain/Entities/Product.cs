using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Product
{
    public Guid Id { get; private set; }
    public string Reference { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public decimal Price { get; private set; }
    public decimal VatRate { get; private set; }
    public bool Active { get; private set; }
    public ProductKind Kind { get; private set; }
    public string? PrimaryImageUrl { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Category Category { get; private set; } = null!;
    public Wine? Wine { get; private set; }
    public ICollection<ProductComponent> Components { get; private set; } = new List<ProductComponent>();
    public ICollection<ProductComponent> UsedInPacks { get; private set; } = new List<ProductComponent>();

    private Product()
    {
    }

    public Product(
        Guid id,
        string reference,
        string name,
        string slug,
        string description,
        Guid categoryId,
        decimal price,
        decimal vatRate,
        bool active,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        ProductKind kind = ProductKind.Standard,
        string? primaryImageUrl = null)
    {
        Id = id;
        Reference = reference;
        Name = name;
        Slug = slug;
        Description = description;
        CategoryId = categoryId;
        Price = price;
        VatRate = vatRate;
        Active = active;
        Kind = kind;
        PrimaryImageUrl = primaryImageUrl;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public void SetPrimaryImageUrl(string url, DateTimeOffset updatedAt)
    {
        if (!IsManagedPrimaryImageUrl(url))
        {
            throw new ArgumentException("No se ha podido guardar la imagen.", nameof(url));
        }

        PrimaryImageUrl = url;
        UpdatedAt = updatedAt;
    }

    public void ClearPrimaryImageUrl(DateTimeOffset updatedAt)
    {
        PrimaryImageUrl = null;
        UpdatedAt = updatedAt;
    }

    public void UpdateCatalog(
        string reference,
        string name,
        string slug,
        string description,
        Guid categoryId,
        decimal price,
        decimal vatRate,
        bool active,
        DateTimeOffset updatedAt)
    {
        Reference = reference;
        Name = name;
        Slug = slug;
        Description = description;
        CategoryId = categoryId;
        Price = price;
        VatRate = vatRate;
        Active = active;
        UpdatedAt = updatedAt;
    }

    public static bool IsManagedPrimaryImageUrl(string? url)
    {
        const string prefix = "/media/products/";
        if (string.IsNullOrEmpty(url) || !url.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = url[prefix.Length..];
        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return false;
        }

        var separator = fileName.IndexOf('.');
        if (separator != 32 || separator != fileName.LastIndexOf('.'))
        {
            return false;
        }

        var extension = fileName[(separator + 1)..];
        if (extension is not ("jpg" or "png" or "webp"))
        {
            return false;
        }

        foreach (var character in fileName.AsSpan(0, separator))
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
