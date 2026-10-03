using System.Globalization;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Products.CreateProduct;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
public sealed class IndexModel(
    IAdminCatalogQueries adminCatalogQueries,
    ICreateCategory createCategory,
    ICreateProduct createProduct) : PageModel
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
