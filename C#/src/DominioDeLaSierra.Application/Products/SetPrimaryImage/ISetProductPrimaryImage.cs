namespace DominioDeLaSierra.Application.Products.SetPrimaryImage;

public interface ISetProductPrimaryImage
{
    Task ExecuteAsync(SetProductPrimaryImageCommand command, CancellationToken cancellationToken = default);
}
