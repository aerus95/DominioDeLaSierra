namespace DominioDeLaSierra.Application.Categories.UpdateCategory;

public interface IUpdateCategory
{
    Task<UpdatedCategoryDto> ExecuteAsync(UpdateCategoryCommand command, CancellationToken cancellationToken = default);
}
