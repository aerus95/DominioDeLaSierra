namespace DominioDeLaSierra.Application.Categories.GetCategories;

public interface ICategoryCatalogQueries
{
    Task<IReadOnlyList<CategoryListItemDto>> GetCategoriesAsync(
        CancellationToken cancellationToken = default);
}
