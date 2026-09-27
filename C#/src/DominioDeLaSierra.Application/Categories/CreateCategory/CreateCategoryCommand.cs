namespace DominioDeLaSierra.Application.Categories.CreateCategory;

public sealed record CreateCategoryCommand(
    string Name,
    string? Slug,
    Guid? ParentCategoryId,
    bool Active = true);
