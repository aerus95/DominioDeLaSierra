using DominioDeLaSierra.Application.Products.ClearPrimaryImage;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class ClearProductPrimaryImageService(
    ApplicationDbContext dbContext,
    IConfiguration configuration) : IClearProductPrimaryImage
{
    public async Task ExecuteAsync(ClearProductPrimaryImageCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ProductId == Guid.Empty)
        {
            throw new ArgumentException("El producto no es válido.", nameof(command));
        }

        var product = await dbContext.Products
            .FirstOrDefaultAsync(item => item.Id == command.ProductId, cancellationToken);
        if (product is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var previousUrl = product.PrimaryImageUrl;
        if (previousUrl is null)
        {
            return;
        }

        product.ClearPrimaryImageUrl(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        var productsDirectory = ManagedProductImageFiles.ProductsDirectory(configuration);
        if (ManagedProductImageFiles.TryResolve(productsDirectory, previousUrl) is { } fullPath)
        {
            ManagedProductImageFiles.DeleteIfExists(fullPath);
        }
    }
}
