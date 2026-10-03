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
}
