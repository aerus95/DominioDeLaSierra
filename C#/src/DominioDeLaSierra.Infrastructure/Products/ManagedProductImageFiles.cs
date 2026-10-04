using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Media;
using Microsoft.Extensions.Configuration;

namespace DominioDeLaSierra.Infrastructure.Products;

internal static class ManagedProductImageFiles
{
    public const string PublicPrefix = "/media/products/";

    public static string ProductsDirectory(IConfiguration configuration)
    {
        var root = ProductMediaOptions.Resolve(configuration["Media:RootPath"]);
        var products = Path.GetFullPath(Path.Combine(root, "products"));
        EnsureInside(root, products);
        Directory.CreateDirectory(products);
        return products;
    }

    public static string AllocateWebpPath(string productsDirectory, out string publicUrl)
    {
        var fileName = $"{Guid.NewGuid():N}.webp";
        var fullPath = SafeFilePath(productsDirectory, fileName);
        publicUrl = PublicPrefix + fileName;
        return fullPath;
    }

    public static string? TryResolve(string productsDirectory, string? url)
    {
        if (!Product.IsManagedPrimaryImageUrl(url))
        {
            return null;
        }

        return SafeFilePath(productsDirectory, url![PublicPrefix.Length..]);
    }

    public static void DeleteIfExists(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SafeFilePath(string productsDirectory, string fileName)
    {
        if (fileName != Path.GetFileName(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\'))
        {
            throw new InvalidOperationException(ProductImageContent.StorageFailureMessage);
        }

        var fullPath = Path.GetFullPath(Path.Combine(productsDirectory, fileName));
        EnsureInside(productsDirectory, fullPath);
        return fullPath;
    }

    private static void EnsureInside(string directory, string candidate)
    {
        var root = Path.GetFullPath(directory);
        var full = Path.GetFullPath(candidate);
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, PathComparison()))
        {
            throw new InvalidOperationException(ProductImageContent.StorageFailureMessage);
        }
    }

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
