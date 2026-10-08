namespace DominioDeLaSierra.Application.Categories.DeleteCategory;

public interface IDeleteCategory
{
    Task<DeletedCategoryDto> ExecuteAsync(DeleteCategoryCommand command, CancellationToken cancellationToken = default);
}
