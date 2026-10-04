using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Products.ClearPrimaryImage;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Application.Products.SetPrimaryImage;
using DominioDeLaSierra.Application.Products.UpdateProduct;
using DominioDeLaSierra.Infrastructure.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
[RequestSizeLimit(ProductMediaOptions.MaxPageRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ProductMediaOptions.MaxPageRequestBytes)]
public sealed class IndexModel(
    IAdminCatalogQueries adminCatalogQueries,
    ICreateCategory createCategory,
    ICreateProduct createProduct,
    ISetProductPrimaryImage setPrimaryImage,
    IClearProductPrimaryImage clearPrimaryImage,
    IValidateProductImport validateProductImport,
    IImportProducts importProducts,
    IUpdateProduct updateProduct) : PageModel
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

    public async Task<IActionResult> OnGetEditProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        if (productId == Guid.Empty)
        {
            return JsonFailure("El producto no es válido.", StatusCodes.Status404NotFound);
        }

        try
        {
            var editor = await adminCatalogQueries.GetProductEditorAsync(productId, cancellationToken);
            if (editor is null)
            {
                return JsonFailure("El producto no existe.", StatusCodes.Status404NotFound);
            }

            return new JsonResult(editor);
        }
        catch (InvalidOperationException exception)
        {
            return JsonFailure(exception.Message, StatusCodes.Status409Conflict);
        }
    }

    public async Task<IActionResult> OnPostEditProductAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        try
        {
            await updateProduct.ExecuteAsync(ReadUpdateCommand(), cancellationToken);
            StatusMessage = $"Producto «{FormValue(Request.Form, "name")?.Trim()}» actualizado correctamente.";
            return new JsonResult(new { ok = true });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return JsonFailure(exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static JsonResult JsonFailure(string message, int statusCode) =>
        new(new { ok = false, message }) { StatusCode = statusCode };

    private UpdateProductCommand ReadUpdateCommand()
    {
        var form = Request.Form;
        var activeSpecified = TryReadExactBool(form, "active", out var active);
        return new UpdateProductCommand(
            ReadFormGuid(form, "productId"),
            FormValue(form, "fichaRevision"),
            FormValue(form, "reference"),
            FormValue(form, "name"),
            FormValue(form, "slug"),
            FormValue(form, "description"),
            FormValue(form, "categoryId"),
            FormValue(form, "price"),
            FormValue(form, "vatRate"),
            activeSpecified ? active : null,
            activeSpecified,
            FormValue(form, "loadedStock"),
            FormValue(form, "stock"),
            form.ContainsKey("stock"),
            FormValue(form, "vintage"),
            FormValue(form, "grape"),
            FormValue(form, "alcohol"),
            FormValue(form, "componentsJson"),
            form.ContainsKey("componentsJson"));
    }

    private static Guid ReadFormGuid(IFormCollection form, string key)
    {
        var raw = FormValue(form, key);
        if (!Guid.TryParse(raw, out var value) || value == Guid.Empty)
        {
            throw new ArgumentException("El producto no es válido.");
        }

        return value;
    }

    private static bool TryReadExactBool(IFormCollection form, string key, out bool value)
    {
        value = false;
        if (!form.TryGetValue(key, out var values) || values.Count != 1)
        {
            return false;
        }

        if (values[0] is not ("true" or "false"))
        {
            return false;
        }

        value = values[0] == "true";
        return true;
    }

    private static string? FormValue(IFormCollection form, string key)
    {
        if (!form.TryGetValue(key, out var values) || values.Count == 0)
        {
            return null;
        }

        return values[0];
    }

    public async Task<IActionResult> OnPostCreateCategoryAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        if (AdminPostedValues.HasBindingError(ModelState, "InputCategory.Active"))
        {
            return JsonFailure("El estado de la categoría no es válido.", StatusCodes.Status400BadRequest);
        }

        if (!TryReadOptionalParent(out var parentId, out var parentError))
        {
            return JsonFailure(parentError, StatusCodes.Status400BadRequest);
        }

        try
        {
            await createCategory.ExecuteAsync(
                new CreateCategoryCommand(InputCategory.Name, InputCategory.Slug, parentId, InputCategory.Active),
                cancellationToken);
            StatusMessage = $"Categoría «{InputCategory.Name}» creada correctamente.";
            return new JsonResult(new { ok = true });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return JsonFailure(exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    public async Task<IActionResult> OnPostCreateProductAsync(IFormFile? primaryImage, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        if (AdminPostedValues.HasBindingError(ModelState, "InputProduct.Active"))
        {
            return JsonFailure("El estado del producto no es válido.", StatusCodes.Status400BadRequest);
        }

        Stream? imageContent = null;
        try
        {
            if (AdminPostedValues.HasBindingError(ModelState, "InputProduct.CategoryId")
                || string.IsNullOrWhiteSpace(InputProduct.CategoryId))
            {
                throw new ArgumentException("Selecciona una categoría.");
            }

            if (!Guid.TryParse(InputProduct.CategoryId, out var categoryId))
            {
                throw new ArgumentException("La categoría seleccionada no es válida.");
            }

            if (string.IsNullOrWhiteSpace(InputProduct.Price))
            {
                throw new ArgumentException("El precio es obligatorio.");
            }

            if (!CatalogNumbers.TryParsePrice(InputProduct.Price, out var price))
            {
                throw new ArgumentException("El precio indicado no es válido.");
            }

            if (string.IsNullOrWhiteSpace(InputProduct.VatRate))
            {
                throw new ArgumentException("El IVA es obligatorio.");
            }

            if (!CatalogNumbers.TryParseVatRate(InputProduct.VatRate, out var vatRate))
            {
                throw new ArgumentException("El IVA indicado no es válido.");
            }

            if (!AdminPostedValues.TryReadProductKind(ModelState, "InputProduct.Kind", out var kind))
            {
                throw new ArgumentException("El tipo de producto no es válido.");
            }

            var initialStock = 0;
            if (kind is ProductKind.Standard or ProductKind.Wine)
            {
                if (AdminPostedValues.HasBindingError(ModelState, "InputProduct.InitialStock"))
                {
                    throw new ArgumentException("El stock inicial indicado no es válido.");
                }

                if (!CatalogNumbers.TryParseInitialStock(InputProduct.InitialStock, out initialStock, out var stockError))
                {
                    throw new ArgumentException(stockError ?? "El stock inicial indicado no es válido.");
                }
            }

            var imageLength = 0L;
            if (primaryImage is { Length: > 0 })
            {
                if (primaryImage.Length > ProductMediaOptions.MaxImageBytes)
                {
                    throw new ArgumentException("La imagen no puede superar 5 MB.");
                }

                imageContent = primaryImage.OpenReadStream();
                imageLength = primaryImage.Length;
            }

            await createProduct.ExecuteAsync(
                new CreateProductCommand(
                    InputProduct.Reference,
                    InputProduct.Name,
                    InputProduct.Slug,
                    InputProduct.Description,
                    categoryId,
                    price,
                    vatRate,
                    InputProduct.Active,
                    initialStock,
                    kind,
                    InputProduct.Vintage,
                    InputProduct.Grape,
                    InputProduct.Alcohol,
                    InputProduct.ComponentsJson,
                    imageContent,
                    imageLength),
                cancellationToken);
            StatusMessage = $"Producto «{InputProduct.Name}» creado correctamente.";
            return new JsonResult(new { ok = true });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            return JsonFailure(exception.Message, StatusCodes.Status400BadRequest);
        }
        finally
        {
            if (imageContent is not null)
            {
                await imageContent.DisposeAsync();
            }
        }
    }

    public async Task<IActionResult> OnGetProductCandidatesAsync(CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        var candidates = await adminCatalogQueries.GetComponentCandidatesAsync(cancellationToken);
        return new JsonResult(candidates);
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

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(imageProductId), imageProductId, out var productId))
        {
            ErrorMessage = "El producto no es válido.";
            return RedirectToPage();
        }

        try
        {
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
                new SetProductPrimaryImageCommand(productId, content, primaryImage.Length),
                cancellationToken);
            StatusMessage = "Imagen actualizada correctamente.";
        }
        catch (IOException)
        {
            ErrorMessage = "No se ha podido guardar la imagen.";
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "No se ha podido guardar la imagen.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
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

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(imageProductId), imageProductId, out var productId))
        {
            ErrorMessage = "El producto no es válido.";
            return RedirectToPage();
        }

        try
        {
            await clearPrimaryImage.ExecuteAsync(
                new ClearProductPrimaryImageCommand(productId),
                cancellationToken);
            StatusMessage = "Imagen eliminada correctamente.";
        }
        catch (IOException)
        {
            ErrorMessage = "No se ha podido guardar la imagen.";
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "No se ha podido guardar la imagen.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
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

        if (AdminPostedValues.HasBindingError(ModelState, "importFile"))
        {
            return new JsonResult(ProductImportReport.Document("El documento no es válido."));
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

        if (AdminPostedValues.HasBindingError(ModelState, "importFile"))
        {
            return new JsonResult(ProductImportReport.Document("El documento no es válido."));
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
        $"Importación completada: {Count(counts.Total, "producto", "productos")} ({Count(counts.Standard, "estándar", "estándares")}, {Count(counts.Wine, "vino", "vinos")}, {Count(counts.Pack, "pack", "packs")}).";

    private static string Count(int value, string singular, string plural) =>
        $"{value} {(value == 1 ? singular : plural)}";

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Categories = await adminCatalogQueries.GetCategoriesAsync(cancellationToken);
        Products = await adminCatalogQueries.GetProductsAsync(cancellationToken);
    }

    private bool TryReadOptionalParent(out Guid? parentId, out string error)
    {
        parentId = null;
        error = string.Empty;
        if (AdminPostedValues.HasBindingError(ModelState, "InputCategory.ParentCategoryId"))
        {
            error = "La categoría padre no es válida.";
            return false;
        }

        var raw = InputCategory.ParentCategoryId?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return true;
        }

        if (!Guid.TryParse(raw, out var parsed) || parsed == Guid.Empty)
        {
            error = "La categoría padre no es válida.";
            return false;
        }

        parentId = parsed;
        return true;
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
        public string InitialStock { get; set; } = "0";
        public string Kind { get; set; } = "Standard";
        public string? Vintage { get; set; }
        public string? Grape { get; set; }
        public string? Alcohol { get; set; }
        public string? ComponentsJson { get; set; }
    }

    public static string StockLabel(AdminProductDto product) =>
        product.Kind == ProductKind.Pack
            ? "Derivado"
            : (product.StockQuantity ?? 0).ToString();
}
