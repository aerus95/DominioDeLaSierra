namespace DominioDeLaSierra.Application.Categories.UpdateCategory;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Name,
    string? Slug,
    Guid? ParentCategoryId,
    bool Active);

public sealed record UpdatedCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId,
    bool Active);
