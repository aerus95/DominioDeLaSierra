namespace DominioDeLaSierra.Domain.Entities;

public sealed class Warehouse
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 150;

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool Active { get; private set; }
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public ICollection<Stock> Stocks { get; private set; } = new List<Stock>();

    private Warehouse()
    {
    }

    public Warehouse(Guid id, string code, string name, bool active, bool isDefault, DateTimeOffset createdAt)
    {
        Id = id;
        Code = code;
        Name = name;
        Active = active;
        IsDefault = isDefault;
        CreatedAt = createdAt;
    }
}
