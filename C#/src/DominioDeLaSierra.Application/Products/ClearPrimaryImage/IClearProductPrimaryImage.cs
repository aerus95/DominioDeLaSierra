namespace DominioDeLaSierra.Application.Products.ClearPrimaryImage;

public interface IClearProductPrimaryImage
{
    Task ExecuteAsync(ClearProductPrimaryImageCommand command, CancellationToken cancellationToken = default);
}
