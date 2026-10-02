namespace DominioDeLaSierra.Application.Categories.CreateCategory;

public interface ICreateCategory
{
    Task<CreatedCategoryDto> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken = default);
}
