using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Products.ClearPrimaryImage;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.GetProducts;
using DominioDeLaSierra.Application.Products.ImportProducts;
using DominioDeLaSierra.Application.Products.SetPrimaryImage;
using DominioDeLaSierra.Application.Products.UpdateProduct;
using DominioDeLaSierra.Infrastructure.Admin;
using DominioDeLaSierra.Infrastructure.Categories;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.Infrastructure.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DominioDeLaSierra.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(PostgresConnection.Build(configuration)));

        services.AddScoped<IProductCatalogQueries, ProductCatalogQueries>();
        services.AddScoped<ICategoryCatalogQueries, CategoryCatalogQueries>();
        services.AddScoped<IAdminCatalogQueries, AdminCatalogQueries>();
        services.AddScoped<IAdminUserAuthentication, AdminUserAuthentication>();
        services.AddScoped<IAdminUserManagement, AdminUserManagement>();
        services.AddScoped<ICreateCategory, CreateCategoryService>();
        services.AddScoped<ICreateProduct, CreateProductService>();
        services.AddScoped<IUpdateProduct, UpdateProductService>();
        services.AddScoped<ISetProductPrimaryImage, SetProductPrimaryImageService>();
        services.AddScoped<IClearProductPrimaryImage, ClearProductPrimaryImageService>();
        services.AddScoped<IValidateProductImport, ValidateProductImportService>();
        services.AddScoped<IImportProducts, ImportProductsService>();

        return services;
    }
}
