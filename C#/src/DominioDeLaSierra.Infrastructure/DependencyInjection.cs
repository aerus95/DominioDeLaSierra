using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Application.Categories.CreateCategory;
using DominioDeLaSierra.Application.Categories.DeleteCategory;
using DominioDeLaSierra.Application.Categories.GetCategories;
using DominioDeLaSierra.Application.Categories.UpdateCategory;
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
using DominioDeLaSierra.Application.Checkout;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Inventory;
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
            options.UseNpgsql(PostgresConnection.Build(configuration))
                .AddInterceptors(new TrackNewPaymentsInterceptor()));

        services.AddScoped<IProductCatalogQueries, ProductCatalogQueries>();
        services.AddScoped<ICategoryCatalogQueries, CategoryCatalogQueries>();
        services.AddScoped<IAdminCatalogQueries, AdminCatalogQueries>();
        services.AddScoped<IAdminOrders, AdminOrderService>();
        services.AddScoped<IAdminUserAuthentication, AdminUserAuthentication>();
        services.AddScoped<IAdminUserManagement, AdminUserManagement>();
        services.AddScoped<ICreateCategory, CreateCategoryService>();
        services.AddScoped<IUpdateCategory, UpdateCategoryService>();
        services.AddScoped<IDeleteCategory, DeleteCategoryService>();
        services.AddScoped<ICreateProduct, CreateProductService>();
        services.AddScoped<IUpdateProduct, UpdateProductService>();
        services.AddScoped<ISetProductPrimaryImage, SetProductPrimaryImageService>();
        services.AddScoped<IClearProductPrimaryImage, ClearProductPrimaryImageService>();
        services.AddScoped<IValidateProductImport, ValidateProductImportService>();
        services.AddScoped<IImportProducts, ImportProductsService>();
        services.AddScoped<IOrderStock, OrderStockService>();
        services.AddScoped<ICheckout, CheckoutService>();
        services.AddScoped<IOrderPaymentSessions, OrderPaymentSessionService>();
        services.AddScoped<IStripeWebhookParser, StripeWebhookParser>();
        services.AddScoped<IStripeWebhooks, StripeWebhookService>();
        services.AddScoped<IReservationSweep, ReservationSweepService>();
        services.AddSingleton<StripeWebhookFailureProbe>();
        services.AddSingleton<ReservationSweepFailureProbe>();
        if (string.Equals(configuration["Stripe:Gateway"], "Stub", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<StripeCheckoutStub>();
            services.AddSingleton<IStripeCheckoutGateway>(provider => provider.GetRequiredService<StripeCheckoutStub>());
        }
        else
        {
            services.AddScoped<IStripeCheckoutGateway, StripeCheckoutGateway>();
        }

        return services;
    }
}
