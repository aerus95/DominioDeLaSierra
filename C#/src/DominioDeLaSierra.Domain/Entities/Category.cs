namespace DominioDeLaSierra.Domain.Entities;

public sealed class Category
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public Guid? ParentCategoryId { get; private set; }
    public bool Active { get; private set; }

    public Category? ParentCategory { get; private set; }
    public ICollection<Category> Children { get; private set; } = new List<Category>();
    public ICollection<Product> Products { get; private set; } = new List<Product>();

    private Category()
    {
    }

    public Category(Guid id, string name, string slug, Guid? parentCategoryId, bool active)
    {
        Id = id;
        Name = name;
        Slug = slug;
        ParentCategoryId = parentCategoryId;
        Active = active;
    }
}
