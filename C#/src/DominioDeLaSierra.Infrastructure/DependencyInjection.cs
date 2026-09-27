using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Products.GetProducts;
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

        return services;
    }
}
