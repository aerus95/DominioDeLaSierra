namespace DominioDeLaSierra.Application.Categories.CreateCategory;

public interface ICreateCategory
{
    Task<Guid> ExecuteAsync(CreateCategoryCommand command, CancellationToken cancellationToken = default);
}
