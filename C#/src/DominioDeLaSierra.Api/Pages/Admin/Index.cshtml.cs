using System.Globalization;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Products.ClearPrimaryImage;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Application.Products.SetPrimaryImage;
using DominioDeLaSierra.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
[RequestSizeLimit(ProductMediaOptions.MaxImageBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ProductMediaOptions.MaxImageBytes)]
public sealed class IndexModel(
    IAdminCatalogQueries adminCatalogQueries,
    ICreateCategory createCategory,
    ICreateProduct createProduct,
    ISetProductPrimaryImage setPrimaryImage,
    IClearProductPrimaryImage clearPrimaryImage,
    IValidateProductImport validateProductImport,
    IImportProducts importProducts) : PageModel
{
    public IReadOnlyList<AdminCategoryDto> Categories { get; private set; } = [];
    public IReadOnlyList<AdminProductDto> Products { get; private set; } = [];

    [BindProperty]
    public CategoryForm InputCategory { get; set; } = new();

    [BindProperty]
    public ProductForm InputProduct { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool CanWrite =>
        User.IsInRole(nameof(AdminRole.Administrator)) || User.IsInRole(nameof(AdminRole.Manager));

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToPage();
        }

        try
        {
            Guid? parentId = Guid.TryParse(InputCategory.ParentCategoryId, out var parsed) ? parsed : null;
            await createCategory.ExecuteAsync(
                new CreateCategoryCommand(InputCategory.Name, InputCategory.Slug, parentId, InputCategory.Active),
                cancellationToken);
            StatusMessage = $"Categoría «{InputCategory.Name}» creada en PostgreSQL.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateProductAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToPage();
        }

        try
        {
            if (!Guid.TryParse(InputProduct.CategoryId, out var categoryId))
            {
                throw new ArgumentException("Selecciona una categoría.");
            }

            var price = ParseDecimal(InputProduct.Price, "precio");
            var vatRate = ParseDecimal(InputProduct.VatRate, "IVA");

            await createProduct.ExecuteAsync(
                new CreateProductCommand(
                    InputProduct.Reference,
                    InputProduct.Name,
                    InputProduct.Slug,
                    InputProduct.Description,
                    categoryId,
                    price,
                    vatRate,
                    InputProduct.Active),
                cancellationToken);
            StatusMessage = $"Producto «{InputProduct.Name}» guardado. Visible en GET /api/v1/products si está activo.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetPrimaryImageAsync(
        Guid imageProductId,
        IFormFile? primaryImage,
        CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToPage();
        }

        try
        {
            if (imageProductId == Guid.Empty)
            {
                throw new ArgumentException("El producto no es válido.");
            }

            if (primaryImage is null || primaryImage.Length == 0)
            {
                throw new ArgumentException("La imagen es obligatoria.");
            }

            if (primaryImage.Length > ProductMediaOptions.MaxImageBytes)
            {
                throw new ArgumentException("La imagen no puede superar 5 MB.");
            }

            await using var content = primaryImage.OpenReadStream();
            await setPrimaryImage.ExecuteAsync(
                new SetProductPrimaryImageCommand(imageProductId, content, primaryImage.Length),
                cancellationToken);
            StatusMessage = "Imagen principal actualizada.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletePrimaryImageAsync(
        Guid imageProductId,
        CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToPage();
        }

        try
        {
            if (imageProductId == Guid.Empty)
            {
                throw new ArgumentException("El producto no es válido.");
            }

            await clearPrimaryImage.ExecuteAsync(
                new ClearProductPrimaryImageCommand(imageProductId),
                cancellationToken);
            StatusMessage = "Imagen principal eliminada.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostValidateProductImportAsync(
        IFormFile? importFile,
        CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return ImportDenied();
        }

        if (!TryOpenImport(importFile, out var content, out var denied))
        {
            return denied!;
        }

        await using (content)
        {
            var report = await validateProductImport.ExecuteAsync(
                new ValidateProductImportCommand(content!, importFile!.Length),
                cancellationToken);
            return new JsonResult(report);
        }
    }

    public async Task<IActionResult> OnPostImportProductsAsync(
        IFormFile? importFile,
        CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return ImportDenied();
        }

        if (!TryOpenImport(importFile, out var content, out var denied))
        {
            return denied!;
        }

        await using (content)
        {
            var report = await importProducts.ExecuteAsync(
                new ImportProductsCommand(content!, importFile!.Length),
                cancellationToken);
            if (report.Imported && report.Counts is { } counts)
            {
                StatusMessage = ImportCompleted(counts);
            }

            return new JsonResult(report);
        }
    }

    public static string KindLabel(ProductKind kind) => kind switch
    {
        ProductKind.Standard => "Estándar",
        ProductKind.Wine => "Vino",
        ProductKind.Pack => "Pack",
        _ => kind.ToString()
    };

    private JsonResult ImportDenied() =>
        new(ProductImportReport.Document("No tienes permiso para modificar el catálogo."))
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    private static bool TryOpenImport(
        IFormFile? importFile,
        out Stream? content,
        out IActionResult? denied)
    {
        if (importFile is null || importFile.Length == 0)
        {
            content = null;
            denied = new JsonResult(ProductImportReport.Document("El documento está vacío."));
            return false;
        }

        content = importFile.OpenReadStream();
        denied = null;
        return true;
    }

    private static string ImportCompleted(ProductImportCounts counts) =>
        $"Importación completada: {Count(counts.Total, "producto", "productos")} ({Count(counts.Standard, "estándar", "estándar")}, {Count(counts.Wine, "vino", "vinos")}, {Count(counts.Pack, "pack", "packs")}).";

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Categories = await adminCatalogQueries.GetCategoriesAsync(cancellationToken);
        Products = await adminCatalogQueries.GetProductsAsync(cancellationToken);
    }

    private static decimal ParseDecimal(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"El {fieldName} es obligatorio.");
        }

        var trimmed = value.Trim().Replace(" ", string.Empty);
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.GetCultureInfo("es-ES"), out var parsedEs))
        {
            return parsedEs;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedInvariant))
        {
            return parsedInvariant;
        }

        throw new ArgumentException($"El {fieldName} no es un número válido.");
    }

    public sealed class CategoryForm
    {
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? ParentCategoryId { get; set; }
        public bool Active { get; set; } = true;
    }

    public sealed class ProductForm
    {
        public string Reference { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string Description { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string VatRate { get; set; } = "21";
        public bool Active { get; set; } = true;
    }
}
