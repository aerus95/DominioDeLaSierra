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
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Application.Inventory;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Infrastructure.Checkout;
using DominioDeLaSierra.Infrastructure.Inventory;
using DominioDeLaSierra.Infrastructure.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        services.AddScoped(provider => RedsysSettings.Read(provider.GetRequiredService<IConfiguration>()));
        services.AddScoped<RedsysPaymentStarter>();
        services.AddSingleton<RedsysNotificationFailureProbe>();
        services.AddScoped<IRedsysNotifications, RedsysNotificationService>();
        services.AddScoped(provider => PayPalSettings.Read(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<PayPalOrderScript>();
        services.AddHttpClient<PayPalSandboxClient>(client =>
        {
            client.BaseAddress = new Uri(PayPalRules.SandboxApiRoot);
            client.Timeout = TimeSpan.FromSeconds(20);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IPayPalOrders>(SelectPayPalOrders);
        services.AddScoped<IPayPalCertificates>(SelectPayPalCertificates);
        services.AddScoped<PayPalPaymentStarter>();
        services.AddScoped<PayPalCheckoutService>();
        services.AddScoped<IPayPalCaptures>(provider => provider.GetRequiredService<PayPalCheckoutService>());
        services.AddScoped<IPayPalWebhooks>(provider => provider.GetRequiredService<PayPalCheckoutService>());
        services.AddScoped<IPayPalObservations>(provider => provider.GetRequiredService<PayPalCheckoutService>());
        services.AddSingleton<IPaymentProviders, PaymentProviderCatalog>();
        services.AddSingleton<IPaymentStatusQuery>(new UnconfiguredPaymentStatusQuery(PaymentProvider.Redsys));
        services.AddScoped<IPaymentStatusQuery, PayPalPaymentStatusQuery>();
        services.AddScoped<IRedsysOperationNumbers, RedsysOperationNumberAllocator>();
        services.AddScoped<IOrderPaymentSessions, OrderPaymentSessionService>();
        services.AddScoped<ICheckoutOutcome, CheckoutOutcomeService>();
        services.AddScoped<IStripeWebhookParser, StripeWebhookParser>();
        services.AddScoped<IStripeWebhooks, StripeWebhookService>();
        services.AddScoped<IReservationSweep, ReservationSweepService>();
        services.AddScoped<IReservationHoldAlerts, ReservationHoldAlertService>();
        services.AddScoped<ReservationHoldIncidentRecorder>();
        services.AddScoped<IProviderPaymentReconciliation, ProviderPaymentReconciliationService>();
        services.AddScoped<IOrderPaymentReconciliation, OrderPaymentReconciliationService>();
        services.AddScoped<IOrderHoldRelease, OrderHoldReleaseService>();
        services.Configure<OrderNotificationOptions>(configuration.GetSection(OrderNotificationOptions.SectionName));
        services.AddScoped<IOrderNotificationDispatch, OrderNotificationDispatchService>();
        services.AddScoped<IOrderMailSender>(provider =>
        {
            var notificationOptions = provider.GetRequiredService<IOptions<OrderNotificationOptions>>().Value;
            return string.Equals(notificationOptions.Delivery, OrderNotificationDelivery.Capture, StringComparison.Ordinal)
                ? new MailpitOrderMailSender(notificationOptions)
                : new DisabledOrderMailSender();
        });
        services.Configure<InvoiceOptions>(configuration.GetSection(InvoiceOptions.SectionName));
        services.AddScoped<IInvoiceIssuance, InvoiceIssuanceService>();
        services.AddSingleton<InvoiceIssuanceFailureProbe>();
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

    private static IPayPalOrders SelectPayPalOrders(IServiceProvider provider)
    {
        return PayPalScript(provider)
            ? provider.GetRequiredService<PayPalOrderScript>()
            : provider.GetRequiredService<PayPalSandboxClient>();
    }

    private static IPayPalCertificates SelectPayPalCertificates(IServiceProvider provider)
    {
        return PayPalScript(provider)
            ? provider.GetRequiredService<PayPalOrderScript>()
            : provider.GetRequiredService<PayPalSandboxClient>();
    }

    private static bool PayPalScript(IServiceProvider provider)
    {
        var environment = provider.GetRequiredService<IHostEnvironment>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        return PayPalProtocol.UseScript(environment.EnvironmentName, configuration["PayPal:Gateway"]);
    }
}
