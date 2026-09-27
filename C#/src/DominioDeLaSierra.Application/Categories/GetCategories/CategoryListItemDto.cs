namespace DominioDeLaSierra.Application.Categories.GetCategories;

public sealed record CategoryListItemDto(
    Guid Id,
    string Name,
    string Slug,
    Guid? ParentCategoryId);
