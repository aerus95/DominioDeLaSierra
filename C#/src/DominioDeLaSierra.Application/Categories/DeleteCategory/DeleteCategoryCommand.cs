namespace DominioDeLaSierra.Application.Categories.DeleteCategory;

public sealed record DeleteCategoryCommand(Guid Id);

public sealed record DeletedCategoryDto(Guid Id, string Name);
