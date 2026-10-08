using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Common;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.DeleteCategory;
using DominioDeLaSierra.Application.Categories.UpdateCategory;
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
    IUpdateCategory updateCategory,
    IDeleteCategory deleteCategory,
    ICreateProduct createProduct,
    ISetProductPrimaryImage setPrimaryImage,
    IClearProductPrimaryImage clearPrimaryImage,
    IValidateProductImport validateProductImport,
    IImportProducts importProducts,
    IUpdateProduct updateProduct) : PageModel
{
    public IReadOnlyList<AdminCategoryDto> Categories { get; private set; } = [];
    public AdminListResult<AdminCategoryDto> CategoryResults { get; private set; } = new([], 0);
    public AdminListResult<AdminProductDto> ProductResults { get; private set; } = new([], 0);
    public Dictionary<string, string> CatalogRoutes { get; private set; } = new();
    public string? ProductQuery { get; private set; }
    public Guid? ProductCategoryId { get; private set; }
    public ProductKind? ProductKindFilter { get; private set; }
    public bool? ProductActive { get; private set; }
    public string? CategoryQuery { get; private set; }
    public bool? CategoryActive { get; private set; }
    public Guid? CategoryParentId { get; private set; }
    public bool CategoryRootsOnly { get; private set; }
    public string? ProductKindValue => ProductKindFilter?.ToString();
    public string? ProductActiveValue => ActiveValue(ProductActive);
    public string? CategoryActiveValue => ActiveValue(CategoryActive);
    public string? CategoryParentValue => CategoryRootsOnly ? "none" : CategoryParentId?.ToString();
    public string ClearProductFiltersUrl => Url.Page(null, ToRouteValues(CategoryRoutes())) ?? "/admin";
    public string ClearCategoryFiltersUrl => Url.Page(null, ToRouteValues(ProductRoutes())) ?? "/admin";

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
        ReadFilters();
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetCategoryRowsAsync(CancellationToken cancellationToken)
    {
        ReadFilters();
        await LoadCategoriesAsync(cancellationToken);
        return Partial("_CategoryFragment", this);
    }

    public async Task<IActionResult> OnGetProductRowsAsync(CancellationToken cancellationToken)
    {
        ReadFilters();
        await LoadProductsAsync(cancellationToken);
        return Partial("_ProductFragment", this);
    }

    public static string? SelectedAttr(bool selected) => selected ? "selected" : null;

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

    public async Task<IActionResult> OnPostUpdateCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        if (AdminPostedValues.HasBindingError(ModelState, "InputCategory.Active"))
        {
            return JsonFailure("El estado de la categoría no es válido.", StatusCodes.Status400BadRequest);
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(categoryId), categoryId, out var id))
        {
            return JsonFailure("La categoría no es válida.", StatusCodes.Status400BadRequest);
        }

        if (!TryReadOptionalParent(out var parentId, out var parentError))
        {
            return JsonFailure(parentError, StatusCodes.Status400BadRequest);
        }

        try
        {
            var updated = await updateCategory.ExecuteAsync(
                new UpdateCategoryCommand(id, InputCategory.Name, InputCategory.Slug, parentId, InputCategory.Active),
                cancellationToken);
            StatusMessage = $"Categoría «{updated.Name}» actualizada correctamente.";
            return new JsonResult(new { ok = true });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return JsonFailure(exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        if (!CanWrite)
        {
            return JsonFailure("No tienes permiso para modificar el catálogo.", StatusCodes.Status403Forbidden);
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(categoryId), categoryId, out var id))
        {
            return JsonFailure("La categoría no es válida.", StatusCodes.Status400BadRequest);
        }

        try
        {
            var deleted = await deleteCategory.ExecuteAsync(new DeleteCategoryCommand(id), cancellationToken);
            StatusMessage = $"Categoría «{deleted.Name}» eliminada correctamente.";
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
        ReadFilters();
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToCatalog();
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(imageProductId), imageProductId, out var productId))
        {
            ErrorMessage = "El producto no es válido.";
            return RedirectToCatalog();
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

        return RedirectToCatalog();
    }

    public async Task<IActionResult> OnPostDeletePrimaryImageAsync(
        Guid imageProductId,
        CancellationToken cancellationToken)
    {
        ReadFilters();
        if (!CanWrite)
        {
            ErrorMessage = "No tienes permiso para modificar el catálogo.";
            return RedirectToCatalog();
        }

        if (!AdminPostedValues.TryReadGuid(ModelState, nameof(imageProductId), imageProductId, out var productId))
        {
            ErrorMessage = "El producto no es válido.";
            return RedirectToCatalog();
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

        return RedirectToCatalog();
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
        await LoadCategoriesAsync(cancellationToken);
        await LoadProductsAsync(cancellationToken);
    }

    private async Task LoadCategoriesAsync(CancellationToken cancellationToken)
    {
        var categoryQuery = CurrentCategoryQuery();
        var allCategories = await adminCatalogQueries.GetCategoriesAsync(
            AdminCategoryListQuery.Unfiltered,
            cancellationToken);
        Categories = allCategories.Items;
        CategoryResults = IsUnfiltered(categoryQuery)
            ? allCategories
            : await adminCatalogQueries.GetCategoriesAsync(categoryQuery, cancellationToken);
    }

    private async Task LoadProductsAsync(CancellationToken cancellationToken)
    {
        ProductResults = await adminCatalogQueries.GetProductsAsync(CurrentProductQuery(), cancellationToken);
    }

    private void ReadFilters()
    {
        ProductQuery = AdminListSearch.Normalize(QueryValue("productQuery"));
        ProductCategoryId = ReadGuid(QueryValue("productCategory"));
        ProductKindFilter = ReadKind(QueryValue("productKind"));
        ProductActive = ReadBool(QueryValue("productActive"));
        CategoryQuery = AdminListSearch.Normalize(QueryValue("categoryQuery"));
        CategoryActive = ReadBool(QueryValue("categoryActive"));
        var parent = QueryValue("categoryParent");
        CategoryRootsOnly = parent is not null && parent.Equals("none", StringComparison.OrdinalIgnoreCase);
        CategoryParentId = CategoryRootsOnly ? null : ReadGuid(parent);
        CatalogRoutes = MergeRoutes(ProductRoutes(), CategoryRoutes());
    }

    private AdminProductListQuery CurrentProductQuery() =>
        new(ProductQuery, ProductCategoryId, ProductKindFilter, ProductActive);

    private AdminCategoryListQuery CurrentCategoryQuery() =>
        new(CategoryQuery, CategoryActive, CategoryParentId, CategoryRootsOnly);

    private static bool IsUnfiltered(AdminCategoryListQuery query) =>
        query.Search is null
        && query.Active is null
        && query.ParentCategoryId is null
        && !query.RootsOnly;

    private string? QueryValue(string key)
    {
        if (!Request.Query.TryGetValue(key, out var values) || values.Count == 0)
        {
            return null;
        }

        return values[0];
    }

    private static Guid? ReadGuid(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
        {
            return null;
        }

        return id;
    }

    private static ProductKind? ReadKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Enum.TryParse<ProductKind>(raw, ignoreCase: true, out var kind) ? kind : null;
    }

    private static bool? ReadBool(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static string? ActiveValue(bool? active) => active switch
    {
        true => "true",
        false => "false",
        _ => null
    };

    private Dictionary<string, string> ProductRoutes()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ProductQuery is not null)
        {
            routes["productQuery"] = ProductQuery;
        }

        if (ProductCategoryId is Guid categoryId)
        {
            routes["productCategory"] = categoryId.ToString();
        }

        if (ProductKindFilter is ProductKind kind)
        {
            routes["productKind"] = kind.ToString();
        }

        if (ProductActive is bool active)
        {
            routes["productActive"] = active ? "true" : "false";
        }

        return routes;
    }

    private Dictionary<string, string> CategoryRoutes()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (CategoryQuery is not null)
        {
            routes["categoryQuery"] = CategoryQuery;
        }

        if (CategoryActive is bool active)
        {
            routes["categoryActive"] = active ? "true" : "false";
        }

        if (CategoryRootsOnly)
        {
            routes["categoryParent"] = "none";
        }
        else if (CategoryParentId is Guid parentId)
        {
            routes["categoryParent"] = parentId.ToString();
        }

        return routes;
    }

    private static Dictionary<string, string> MergeRoutes(
        Dictionary<string, string> products,
        Dictionary<string, string> categories)
    {
        var routes = new Dictionary<string, string>(products, StringComparer.Ordinal);
        foreach (var pair in categories)
        {
            routes[pair.Key] = pair.Value;
        }

        return routes;
    }

    private static RouteValueDictionary ToRouteValues(Dictionary<string, string> routes)
    {
        var values = new RouteValueDictionary();
        foreach (var pair in routes)
        {
            values[pair.Key] = pair.Value;
        }

        return values;
    }

    private RedirectToPageResult RedirectToCatalog() => RedirectToPage(ToRouteValues(CatalogRoutes));

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
