using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Infrastructure.Persistence;

namespace DominioDeLaSierra.Infrastructure.Products;

public sealed class ValidateProductImportService(ApplicationDbContext dbContext) : IValidateProductImport
{
    public async Task<ProductImportReport> ExecuteAsync(
        ValidateProductImportCommand command,
        CancellationToken cancellationToken = default)
    {
        var inspection = await ProductImportInspection.InspectAsync(
            dbContext,
            command.Content,
            command.Length,
            cancellationToken);
        return inspection.Report;
    }
}
