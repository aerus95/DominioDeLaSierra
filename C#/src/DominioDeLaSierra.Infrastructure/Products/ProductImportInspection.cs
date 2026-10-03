using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DominioDeLaSierra.Infrastructure.Products;

internal sealed record ProductImportInspectionResult(
    ProductImportReport Report,
    IReadOnlyList<ImportBatchProduct>? Batch);

internal static class ProductImportInspection
{
    public static async Task<ProductImportInspectionResult> InspectAsync(
        ApplicationDbContext dbContext,
        Stream content,
        long length,
        CancellationToken cancellationToken)
    {
        var read = await ProductImportReader.ReadAsync(content, length, cancellationToken);
        if (!read.Stop)
        {
            await ApplyCatalogAsync(dbContext, read.Drafts, cancellationToken);
        }

        var documentErrors = read.DocumentErrors.ToArray();
        var productErrors = read.Drafts
            .Where(draft => draft.Issues.Count > 0)
            .Select(draft => new ProductImportProductErrors(draft.Label, draft.Issues.ToArray()))
            .ToArray();
        if (documentErrors.Length > 0 || productErrors.Length > 0 || read.Stop)
        {
            return new ProductImportInspectionResult(
                new ProductImportReport(false, false, documentErrors, productErrors, null),
                null);
        }

        var batch = BuildBatch(read.Drafts);
        var counts = Count(read.Drafts);
        return new ProductImportInspectionResult(
            new ProductImportReport(true, false, [], [], counts),
            batch);
    }

    private static async Task ApplyCatalogAsync(
        ApplicationDbContext dbContext,
        List<ProductImportDraft> drafts,
        CancellationToken cancellationToken)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var categorySlugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var draft in drafts)
        {
            if (draft.ReferenceValid)
            {
                references.Add(draft.Reference!);
            }

            if (draft.SlugValid)
            {
                slugs.Add(draft.Slug!);
            }

            if (draft.CategoryValid)
            {
                categorySlugs.Add(draft.CategorySlug!);
            }

            foreach (var component in draft.Components)
            {
                if (component.ReferenceValid)
                {
                    references.Add(component.Reference!);
                }
            }
        }

        var categories = await LoadCategoriesAsync(dbContext, categorySlugs.ToList(), cancellationToken);
        var existing = await LoadProductsAsync(dbContext, references.ToList(), slugs.ToList(), cancellationToken);
        var byReference = existing.ToDictionary(product => product.Reference, StringComparer.Ordinal);
        var bySlug = existing.ToDictionary(product => product.Slug, StringComparer.Ordinal);
        var inFile = drafts
            .Where(draft => draft.ReferenceValid)
            .GroupBy(draft => draft.Reference!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var draft in drafts)
        {
            if (draft.ReferenceValid && byReference.ContainsKey(draft.Reference!))
            {
                draft.Issues.Add($"Ya existe un producto con la referencia «{draft.Reference}».");
            }

            if (draft.SlugValid && bySlug.ContainsKey(draft.Slug!))
            {
                draft.Issues.Add($"Ya existe un producto con el slug «{draft.Slug}».");
            }

            if (draft.CategoryValid)
            {
                if (!categories.TryGetValue(draft.CategorySlug!, out var categoryId))
                {
                    draft.Issues.Add($"La categoría «{draft.CategoryInput}» no existe.");
                }
                else
                {
                    draft.CategoryId = categoryId;
                }
            }

            foreach (var component in draft.Components.Where(component => component.IsComplete))
            {
                ResolveComponent(draft, component, inFile, byReference);
            }
        }
    }

    private static void ResolveComponent(
        ProductImportDraft draft,
        ProductImportComponentDraft component,
        Dictionary<string, List<ProductImportDraft>> inFile,
        Dictionary<string, ExistingImportedProduct> existingByReference)
    {
        var reference = component.Reference!;
        if (draft.ReferenceValid && string.Equals(reference, draft.Reference, StringComparison.Ordinal))
        {
            draft.Issues.Add($"El componente «{reference}» es el propio producto.");
            return;
        }

        if (inFile.TryGetValue(reference, out var targets))
        {
            if (targets.Any(target => target.Kind == ProductKind.Pack))
            {
                draft.Issues.Add($"El componente «{reference}» es un pack.");
                return;
            }

            if (targets.Any(target => target.Kind is ProductKind.Standard or ProductKind.Wine))
            {
                component.ResolvesInsideFile = true;
                return;
            }

            draft.Issues.Add($"El componente «{reference}» no es un producto estándar o vino.");
            return;
        }

        if (!existingByReference.TryGetValue(reference, out var existing))
        {
            draft.Issues.Add($"El componente «{reference}» no existe.");
            return;
        }

        if (existing.Kind == ProductKind.Pack)
        {
            draft.Issues.Add($"El componente «{reference}» es un pack.");
            return;
        }

        if (existing.Kind is not (ProductKind.Standard or ProductKind.Wine))
        {
            draft.Issues.Add($"El componente «{reference}» no es un producto estándar o vino.");
            return;
        }

        component.ExistingProductId = existing.Id;
    }

    private static async Task<Dictionary<string, Guid>> LoadCategoriesAsync(
        ApplicationDbContext dbContext,
        List<string> slugs,
        CancellationToken cancellationToken)
    {
        if (slugs.Count == 0)
        {
            return new Dictionary<string, Guid>(StringComparer.Ordinal);
        }

        var rows = await dbContext.Categories
            .AsNoTracking()
            .Where(category => slugs.Contains(category.Slug))
            .Select(category => new { category.Id, category.Slug })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(category => category.Slug, category => category.Id, StringComparer.Ordinal);
    }

    private static async Task<List<ExistingImportedProduct>> LoadProductsAsync(
        ApplicationDbContext dbContext,
        List<string> references,
        List<string> slugs,
        CancellationToken cancellationToken)
    {
        if (references.Count == 0 && slugs.Count == 0)
        {
            return [];
        }

        IQueryable<DominioDeLaSierra.Domain.Entities.Product> query = dbContext.Products.AsNoTracking();
        if (references.Count > 0 && slugs.Count > 0)
        {
            query = query.Where(product => references.Contains(product.Reference) || slugs.Contains(product.Slug));
        }
        else if (references.Count > 0)
        {
            query = query.Where(product => references.Contains(product.Reference));
        }
        else
        {
            query = query.Where(product => slugs.Contains(product.Slug));
        }

        var rows = await query
            .Select(product => new { product.Id, product.Reference, product.Slug, product.Kind })
            .ToListAsync(cancellationToken);
        return rows
            .Select(product => new ExistingImportedProduct(product.Id, product.Reference, product.Slug, product.Kind))
            .ToList();
    }

    private static List<ImportBatchProduct> BuildBatch(List<ProductImportDraft> drafts)
    {
        foreach (var draft in drafts)
        {
            draft.NewId = Guid.NewGuid();
        }

        var byReference = drafts.ToDictionary(draft => draft.Reference!, StringComparer.Ordinal);
        var batch = new List<ImportBatchProduct>(drafts.Count);
        foreach (var draft in drafts)
        {
            var row = new ImportBatchProduct
            {
                Id = draft.NewId,
                Reference = draft.Reference!,
                Name = draft.Name!,
                Slug = draft.Slug!,
                Description = draft.Description,
                CategoryId = draft.CategoryId!.Value,
                Price = draft.Price,
                VatRate = draft.VatRate,
                Active = draft.Active,
                Kind = draft.Kind!.Value,
                Vintage = draft.Kind == ProductKind.Wine ? draft.Vintage : null,
                Grape = draft.Kind == ProductKind.Wine ? draft.Grape : null,
                AlcoholPercent = draft.Kind == ProductKind.Wine ? draft.AlcoholPercent : null
            };

            if (draft.Kind == ProductKind.Pack)
            {
                foreach (var component in draft.Components.Where(component => component.IsComplete))
                {
                    var productId = component.ResolvesInsideFile
                        ? byReference[component.Reference!].NewId
                        : component.ExistingProductId!.Value;
                    row.Components.Add((productId, component.Quantity));
                }
            }

            batch.Add(row);
        }

        return batch;
    }

    private static ProductImportCounts Count(List<ProductImportDraft> drafts)
    {
        var standard = drafts.Count(draft => draft.Kind == ProductKind.Standard);
        var wine = drafts.Count(draft => draft.Kind == ProductKind.Wine);
        var pack = drafts.Count(draft => draft.Kind == ProductKind.Pack);
        return new ProductImportCounts(drafts.Count, standard, wine, pack);
    }
}
