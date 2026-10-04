using DominioDeLaSierra.Application.Products.SetPrimaryImage;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class SetProductPrimaryImageService(
    ApplicationDbContext dbContext,
    IConfiguration configuration) : ISetProductPrimaryImage
{
    public async Task ExecuteAsync(SetProductPrimaryImageCommand command, CancellationToken cancellationToken = default)
    {
        if (command.ProductId == Guid.Empty)
        {
            throw new ArgumentException("El producto no es válido.", nameof(command));
        }

        var webp = await ProductImageContent.PrepareAsync(command.Content, command.Length, cancellationToken);
        var productsDirectory = ManagedProductImageFiles.ProductsDirectory(configuration);
        var fullPath = ManagedProductImageFiles.AllocateWebpPath(productsDirectory, out var publicUrl);

        var product = await dbContext.Products
            .FirstOrDefaultAsync(item => item.Id == command.ProductId, cancellationToken);
        if (product is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var previousUrl = product.PrimaryImageUrl;
        await ProductImageContent.WriteAsync(fullPath, webp, cancellationToken);

        try
        {
            product.SetPrimaryImageUrl(publicUrl, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            ManagedProductImageFiles.DeleteIfExists(fullPath);
            if (exception is DbUpdateException)
            {
                throw new InvalidOperationException(ProductImageContent.StorageFailureMessage, exception);
            }

            throw;
        }

        if (ManagedProductImageFiles.TryResolve(productsDirectory, previousUrl) is { } previousPath
            && !string.Equals(previousPath, fullPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            ManagedProductImageFiles.DeleteIfExists(previousPath);
        }
    }
}
