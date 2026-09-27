using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Products.CreateProduct;
using DominioDeLaSierra.Application.Products.GetProducts;
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
        services.AddScoped<ICreateCategory, CreateCategoryService>();
        services.AddScoped<ICreateProduct, CreateProductService>();

        return services;
    }
}
