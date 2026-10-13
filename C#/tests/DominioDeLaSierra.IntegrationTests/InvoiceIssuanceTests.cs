using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Invoicing;
using DominioDeLaSierra.Infrastructure.Persistence;
using DominioDeLaSierra.TestDatabase;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Pdf.IO;

namespace DominioDeLaSierra.IntegrationTests;

public sealed class InvoiceIssuanceTests(PostgresFixture fixture) : DatabaseTest(fixture)
{
    [Fact]
    public async Task Disabled_issuance_blocks_the_panel_and_a_direct_post()
    {
        await TestCatalog.SeedAsync();
        var orderId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        using var client = Client(Fixture.Factory);
        await LoginAsync(client, TestAccounts.AdminUsername);

        var html = Text(await client.GetStringAsync($"/admin/orders/{orderId}"));
        Assert.Contains("Facturación pendiente de configuración fiscal.", html);
        Assert.Contains("type=\"button\" disabled", html);

        var posted = await PostInvoiceAsync(client, orderId);
        Assert.Contains("Facturación pendiente de configuración fiscal.", posted);
        var counts = await CountsAsync();
        Assert.Equal(0, counts.Invoices);
        Assert.Equal(0, counts.Series);
    }

    [Fact]
    public async Task Rejects_pending_payment_viewer_and_anonymous()
    {
        await TestCatalog.SeedAsync();
        var pendingId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m, paid: false);
        var paidId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        await using var issuing = IssuingFactory();

        using (var admin = Client(issuing))
        {
            await LoginAsync(admin, TestAccounts.AdminUsername);
            var pendingPage = Text(await admin.GetStringAsync($"/admin/orders/{pendingId}"));
            Assert.Contains(InvoiceIssuanceRules.UnpaidMessage, pendingPage);
            Assert.DoesNotContain("Descargar factura", pendingPage);
            var posted = await PostInvoiceAsync(admin, pendingId);
            Assert.Contains(InvoiceIssuanceRules.UnpaidMessage, posted);
        }

        using (var viewer = Client(issuing))
        {
            await LoginAsync(viewer, TestAccounts.ViewerUsername);
            var page = Text(await viewer.GetStringAsync($"/admin/orders/{paidId}"));
            Assert.DoesNotContain("Facturación", page);
            var token = await TokenAsync(viewer, paidId);
            var response = await viewer.PostAsync(
                $"/admin/orders/{paidId}?handler=DownloadInvoice",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var anonymous = issuing.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var anonymousResponse = await anonymous.PostAsync($"/admin/orders/{paidId}?handler=DownloadInvoice", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/admin/login", anonymousResponse.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        var counts = await CountsAsync();
        Assert.Equal(0, counts.Invoices);
        Assert.Equal(0, counts.Series);
    }

    [Fact]
    public async Task Test_mode_issues_without_seller_or_shipping_vat_and_live_mode_blocks_both()
    {
        var testOrder = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_test_");
        var withoutVat = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: null, session: "cs_live_");
        var withoutSeller = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_live_");
        await using var testMode = IssuingFactory(seller: false);
        await using var liveWithoutSeller = LiveFactory(seller: false);
        await using var live = LiveFactory();

        var testFile = await DownloadAsync(testMode, testOrder);
        Assert.True(testFile.Fictional);
        Assert.Contains(Invoice.TestBanner, InvoicePdfInspection.Read(testFile.Content));

        var sellerResult = await Assert.ThrowsAsync<InvoiceIssuanceException>(() => DownloadAsync(liveWithoutSeller, withoutSeller));
        Assert.Equal(InvoiceIssuanceRules.SellerMessage, sellerResult.Message);
        var shippingResult = await Assert.ThrowsAsync<InvoiceIssuanceException>(() => DownloadAsync(live, withoutVat));
        Assert.Equal(InvoiceIssuanceRules.ShippingVatMessage, shippingResult.Message);

        var counts = await CountsAsync();
        Assert.Equal(1, counts.Invoices);
        Assert.Equal(1, counts.Series);
    }

    [Fact]
    public async Task Issues_one_document_and_later_downloads_keep_it()
    {
        await TestCatalog.SeedAsync();
        var orderId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m, customer: "Comprador de prueba");
        await using var issuing = IssuingFactory();
        using var client = Client(issuing);
        await LoginAsync(client, TestAccounts.ManagerUsername);

        var firstResponse = await PostDownloadAsync(client, orderId);
        var first = await ReadPdfAsync(firstResponse);
        var secondResponse = await PostDownloadAsync(client, orderId);
        var second = await ReadPdfAsync(secondResponse);

        Assert.Equal("ficticio-F2026-000001.pdf", first.FileName);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Equal(first.Text, second.Text);
        Assert.Contains("F2026-000001", first.Text);
        Assert.Contains("Emisor ficticio de pruebas", first.Text);
        Assert.Contains("FICTICIO-TEST", first.Text);
        Assert.Contains("Comprador de prueba", first.Text);
        Assert.Contains(Invoice.FictionalBanner, first.Text);
        Assert.DoesNotContain("example.test", first.Text);
        Assert.DoesNotContain("600111222", first.Text);
        Assert.DoesNotContain("cs_test_", first.Text);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Orders" SET "CustomerName" = 'Nombre posterior' WHERE "Id" = {orderId}""");
        var stored = await db.Invoices.AsNoTracking().SingleAsync();
        var again = await DownloadAsync(issuing, orderId);

        Assert.Equal("Comprador de prueba", stored.BuyerName);
        Assert.Equal(stored.FullNumber, again.FullNumber);
        Assert.True((stored.IssuedAt - again.IssuedAt).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.Equal(stored.TotalCents, again.TotalCents);
        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.DoesNotContain("Nombre posterior", InvoicePdfInspection.Read(again.Content));
    }

    [Fact]
    public async Task Simultaneous_requests_for_one_order_create_one_invoice()
    {
        var orderId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        await using var issuing = IssuingFactory();
        var ready = new Barrier(2);
        var first = WaitAndDownload(issuing, orderId, ready);
        var second = WaitAndDownload(issuing, orderId, ready);
        var files = await Task.WhenAll(first, second);

        Assert.Equal(files[0].FullNumber, files[1].FullNumber);
        Assert.Equal(files[0].TotalCents, files[1].TotalCents);
        Assert.True((files[0].IssuedAt - files[1].IssuedAt).Duration() < TimeSpan.FromMilliseconds(1));
        Assert.Equal("F2026-000001", files[0].FullNumber);
        var counts = await CountsAsync();
        Assert.Equal(1, counts.Invoices);
    }

    [Fact]
    public async Task A_rolled_back_issue_does_not_leave_a_gap()
    {
        var firstId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        var failedId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        var thirdId = await SavePaidOrderAsync(taxId: "FICTICIO-TEST", shippingVat: 21m);
        await using var issuing = IssuingFactory();
        var probe = issuing.Services.GetRequiredService<InvoiceIssuanceFailureProbe>();
        probe.Reset();

        var first = await DownloadAsync(issuing, firstId);
        probe.FailNext();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => DownloadAsync(issuing, failedId));
        }
        finally
        {
            probe.Reset();
        }

        var third = await DownloadAsync(issuing, thirdId);
        var recovered = await DownloadAsync(issuing, failedId);

        Assert.Equal("F2026-000001", first.FullNumber);
        Assert.Equal("F2026-000002", third.FullNumber);
        Assert.Equal("F2026-000003", recovered.FullNumber);
        var counts = await CountsAsync();
        Assert.Equal(3, counts.Invoices);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(4, await db.InvoiceSeries.Select(series => series.NextNumber).SingleAsync());
    }

    [Fact]
    public async Task A_pdf_failure_does_not_persist_the_invoice_or_consume_a_number()
    {
        var orderId = await SaveStripeTestOrderAsync();
        await using var issuing = IssuingFactory();
        InvoicePdf.FailNextRender();
        try
        {
            var result = await Assert.ThrowsAsync<InvoiceIssuanceException>(() => DownloadAsync(issuing, orderId));
            Assert.Equal(InvoiceIssuanceRules.PdfMessage, result.Message);
        }
        finally
        {
            InvoicePdf.ResetRenderFailures();
        }

        var counts = await CountsAsync();
        Assert.Equal(0, counts.Invoices);
        Assert.Equal(0, counts.Series);
        var recovered = await DownloadAsync(issuing, orderId);
        Assert.Equal("F2026-000001", recovered.FullNumber);
    }

    [Fact]
    public async Task Stripe_test_order_of_seventeen_euros_gets_a_non_fiscal_document_without_changing_the_order()
    {
        await TestCatalog.SeedAsync();
        var orderId = await SaveStripeTestOrderAsync();
        await using var issuing = IssuingFactory(fictional: false);
        using var client = Client(issuing);
        await LoginAsync(client, TestAccounts.AdminUsername);

        var html = Text(await client.GetStringAsync($"/admin/orders/{orderId}"));
        Assert.Contains(Invoice.TestBanner, html);
        Assert.Contains("Descargar documento de prueba", html);
        Assert.DoesNotContain("type=\"button\" disabled", html);

        var response = await PostDownloadAsync(client, orderId);
        var file = await ReadPdfAsync(response);
        Assert.Equal("ficticio-F2026-000001.pdf", file.FileName);
        Assert.Contains(Invoice.TestBanner, file.Text);
        Assert.Contains(Invoice.MissingTaxIdClarification, file.Text);
        Assert.Contains(Invoice.UndeterminedShippingClarification, file.Text);
        Assert.Contains(TestDocumentIssuer.LegalName, file.Text);
        Assert.Contains(TestDocumentIssuer.TaxId, file.Text);
        Assert.Contains("17,00 EUR", file.Text);
        Assert.DoesNotContain("Factura simplificada", file.Text);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var order = await db.Orders.AsNoTracking().SingleAsync(item => item.Id == orderId);
        var invoice = await db.Invoices.AsNoTracking().SingleAsync();
        Assert.Equal(900, order.ProductSubtotalCents);
        Assert.Equal(800, order.ShippingCents);
        Assert.Equal(1700, order.TotalCents);
        Assert.Null(order.CustomerTaxId);
        Assert.Null(order.ShippingVatRate);
        Assert.True(invoice.Fictional);
        Assert.Equal(FiscalDocumentForm.NotApplicable, invoice.FiscalForm);
        Assert.Null(invoice.BuyerTaxId);
        Assert.Null(invoice.ShippingVatRate);
        Assert.Equal(TestDocumentIssuer.TaxId, invoice.SellerTaxId);
        Assert.Equal("F2026-000001", invoice.FullNumber);
    }

    [Fact]
    public async Task A_test_document_stays_non_fiscal_when_configuration_changes()
    {
        var orderId = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_test_");
        await using var issuing = IssuingFactory(fictional: false);
        var first = await DownloadAsync(issuing, orderId);
        await using var live = LiveFactory();
        var again = await DownloadAsync(live, orderId);

        Assert.True(first.Fictional);
        Assert.True(again.Fictional);
        Assert.Equal(first.FullNumber, again.FullNumber);
        Assert.Contains(Invoice.TestBanner, InvoicePdfInspection.Read(again.Content));
        Assert.DoesNotContain("Factura simplificada", InvoicePdfInspection.Read(again.Content));

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.Invoices.AsNoTracking().SingleAsync();
        Assert.True(stored.Fictional);
        Assert.Equal(FiscalDocumentForm.NotApplicable, stored.FiscalForm);
        Assert.Equal(1, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task Live_stripe_cannot_turn_a_test_payment_into_a_fiscal_invoice_or_a_simplified_one()
    {
        var testPayment = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_test_");
        var withoutTaxId = await SavePaidOrderAsync(taxId: null, shippingVat: 21m, session: "cs_live_");
        var fiscalOrder = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_live_");
        await using var live = LiveFactory();

        var testDocument = await DownloadAsync(live, testPayment);
        Assert.True(testDocument.Fictional);
        Assert.Contains(Invoice.TestBanner, InvoicePdfInspection.Read(testDocument.Content));

        var missingTaxId = await Assert.ThrowsAsync<InvoiceIssuanceException>(() => DownloadAsync(live, withoutTaxId));
        Assert.Equal(InvoiceIssuanceRules.InvoiceFormMessage, missingTaxId.Message);

        var fiscal = await DownloadAsync(live, fiscalOrder);
        Assert.False(fiscal.Fictional);
        Assert.Equal($"factura-{fiscal.FullNumber}.pdf", fiscal.FileName);
        Assert.DoesNotContain(Invoice.TestBanner, InvoicePdfInspection.Read(fiscal.Content));

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var forms = await db.Invoices.AsNoTracking().Select(invoice => invoice.FiscalForm).ToListAsync();
        Assert.Contains(FiscalDocumentForm.NotApplicable, forms);
        Assert.Contains(FiscalDocumentForm.Complete, forms);
        Assert.DoesNotContain(FiscalDocumentForm.Simplified, forms);
    }

    [Fact]
    public async Task Stripe_test_mode_refuses_a_fiscal_invoice_even_when_the_fictional_flag_is_off()
    {
        var orderId = await SavePaidOrderAsync(taxId: "B12345678", shippingVat: 21m, session: "cs_live_");
        await using var testMode = IssuingFactory(fictional: false);

        var result = await Assert.ThrowsAsync<InvoiceIssuanceException>(() => DownloadAsync(testMode, orderId));
        Assert.Equal(InvoiceIssuanceRules.MixedStripeMessage, result.Message);
        var counts = await CountsAsync();
        Assert.Equal(0, counts.Invoices);
        Assert.Equal(0, counts.Series);
    }

    private async Task<Guid> SaveStripeTestOrderAsync()
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var product = await harness.CreateProductAsync(categoryId, "INV-" + Guid.NewGuid().ToString("N")[..8], ProductKind.Wine, vintage: "2024");
        var createdAt = new DateTimeOffset(2026, 10, 12, 18, 0, 0, TimeSpan.Zero);
        const long cents = 900;
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-20261012-" + Guid.NewGuid().ToString("N")[..8],
            Guid.NewGuid().ToString("N"),
            "Comprador de prueba",
            "ficticio-correo@example.test",
            "600111222",
            "Calle Entrega 2",
            "37001",
            "Avila",
            "Salamanca",
            null,
            [new OrderLine(product.Id, "Vino de prueba", product.Reference, ProductKind.Wine, 1, cents, 21m, null)],
            createdAt);
        var payment = order.AddPayment(Guid.NewGuid(), createdAt);
        order.AssignHostedCheckout(
            payment.Id,
            "cs_test_" + Guid.NewGuid().ToString("N"),
            "https://checkout.stripe.com/c/pay/cs_test_audit",
            createdAt.AddMinutes(30));
        order.MarkPaid(payment.Id, createdAt.AddMinutes(1));

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        Assert.Equal(1700, order.TotalCents);
        Assert.Equal(800, order.ShippingCents);
        Assert.Null(order.CustomerTaxId);
        Assert.Null(order.ShippingVatRate);
        return order.Id;
    }

    private async Task<Guid> SavePaidOrderAsync(
        string? taxId,
        decimal? shippingVat,
        bool paid = true,
        string customer = "Comprador de prueba",
        string? session = null)
    {
        var harness = new CatalogHarness(Fixture.Factory.Services);
        var categoryId = await harness.CreateCategoryAsync();
        var product = await harness.CreateProductAsync(categoryId, "INV-" + Guid.NewGuid().ToString("N")[..8], ProductKind.Wine, vintage: "2024");
        var createdAt = new DateTimeOffset(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);
        var cents = OrderAmounts.PriceToCents(product.Price);
        var order = Order.CreatePending(
            Guid.NewGuid(),
            "DS-TEST-" + Guid.NewGuid().ToString("N")[..8],
            Guid.NewGuid().ToString("N"),
            customer,
            "ficticio-correo@example.test",
            "600111222",
            "Calle Entrega 2",
            "37001",
            "Avila",
            "Salamanca",
            null,
            [new OrderLine(product.Id, "Vino de prueba", product.Reference, ProductKind.Wine, 1, cents, product.VatRate, null)],
            createdAt);
        if (taxId is not null)
        {
            order.AssignCustomerTaxId(taxId);
        }

        if (shippingVat is not null)
        {
            order.DefineShippingVat(shippingVat.Value);
        }

        var payment = order.AddPayment(Guid.NewGuid(), createdAt);
        if (session is not null)
        {
            order.AssignCheckoutSession(payment.Id, session + Guid.NewGuid().ToString("N"));
        }

        if (paid)
        {
            order.MarkPaid(payment.Id, createdAt.AddMinutes(1));
        }

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private WebApplicationFactory<Program> IssuingFactory(bool seller = true, bool fictional = true)
    {
        return Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Stripe:Gateway", "Stub");
            builder.UseSetting("InvoiceIssuance:Enabled", "true");
            builder.UseSetting("InvoiceIssuance:Fictional", fictional ? "true" : "false");
            builder.UseSetting("InvoiceIssuance:Series", "F2026");
            builder.UseSetting("InvoiceIssuance:FirstNumber", "1");
            builder.UseSetting("InvoiceIssuance:Seller:LegalName", seller ? "Emisor ficticio de pruebas" : "");
            builder.UseSetting("InvoiceIssuance:Seller:TaxId", seller ? "FICTICIO-TEST" : "");
            builder.UseSetting("InvoiceIssuance:Seller:AddressLine", seller ? "Calle Ficticia 1" : "");
            builder.UseSetting("InvoiceIssuance:Seller:PostalCode", seller ? "00000" : "");
            builder.UseSetting("InvoiceIssuance:Seller:City", seller ? "Ciudad de prueba" : "");
            builder.UseSetting("InvoiceIssuance:Seller:Province", seller ? "Provincia de prueba" : "");
            builder.UseSetting("InvoiceIssuance:Seller:CountryCode", seller ? "ES" : "");
        });
    }

    private WebApplicationFactory<Program> LiveFactory(bool seller = true, bool fictional = false)
    {
        return Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Stripe:Gateway", "Stripe");
            builder.UseSetting("Stripe:SecretKey", "sk_live_5123456789abcdef");
            builder.UseSetting("Checkout:Access", "Closed");
            builder.UseSetting("Notifications:DispatchEnabled", "false");
            builder.UseSetting("Notifications:Delivery", "Disabled");
            builder.UseSetting("InvoiceIssuance:Enabled", "true");
            builder.UseSetting("InvoiceIssuance:Fictional", fictional ? "true" : "false");
            builder.UseSetting("InvoiceIssuance:Series", "F2026");
            builder.UseSetting("InvoiceIssuance:FirstNumber", "1");
            builder.UseSetting("InvoiceIssuance:Seller:LegalName", seller ? "Dominio fiscal de prueba" : "");
            builder.UseSetting("InvoiceIssuance:Seller:TaxId", seller ? "B00000000" : "");
            builder.UseSetting("InvoiceIssuance:Seller:AddressLine", seller ? "Calle Fiscal 1" : "");
            builder.UseSetting("InvoiceIssuance:Seller:PostalCode", seller ? "37000" : "");
            builder.UseSetting("InvoiceIssuance:Seller:City", seller ? "Salamanca" : "");
            builder.UseSetting("InvoiceIssuance:Seller:Province", seller ? "Salamanca" : "");
            builder.UseSetting("InvoiceIssuance:Seller:CountryCode", seller ? "ES" : "");
        });
    }

    private static async Task<InvoiceFile> DownloadAsync(WebApplicationFactory<Program> factory, Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoiceIssuance>().DownloadAsync(orderId);
    }

    private static async Task<InvoiceFile> WaitAndDownload(WebApplicationFactory<Program> factory, Guid orderId, Barrier ready)
    {
        await Task.Yield();
        ready.SignalAndWait();
        return await DownloadAsync(factory, orderId);
    }

    private async Task<(int Invoices, int Series)> CountsAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Invoices.CountAsync(), await db.InvoiceSeries.CountAsync());
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        return new HttpClient(new CookieHandler { InnerHandler = factory.Server.CreateHandler() })
        {
            BaseAddress = factory.Server.BaseAddress
        };
    }

    private static async Task LoginAsync(HttpClient client, string username)
    {
        var page = await client.GetAsync("/admin/login");
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = TestAccounts.Password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string Text(string html) => WebUtility.HtmlDecode(html);

    private static async Task<string> TokenAsync(HttpClient client, Guid orderId)
    {
        var html = await client.GetStringAsync($"/admin/orders/{orderId}");
        return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static async Task<string> PostInvoiceAsync(HttpClient client, Guid orderId)
    {
        var token = await TokenAsync(client, orderId);
        var response = await client.PostAsync(
            $"/admin/orders/{orderId}?handler=DownloadInvoice",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found)
        {
            var location = response.Headers.Location;
            var path = location is null
                ? $"/admin/orders/{orderId}"
                : location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            return Text(await client.GetStringAsync(path));
        }

        return Text(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostDownloadAsync(HttpClient client, Guid orderId)
    {
        var token = await TokenAsync(client, orderId);
        var response = await client.PostAsync(
            $"/admin/orders/{orderId}?handler=DownloadInvoice",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        return response;
    }

    private static async Task<(string FileName, string Text)> ReadPdfAsync(HttpResponseMessage response)
    {
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes[..5]));
        using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 1);
        return (fileName ?? string.Empty, InvoicePdfInspection.Read(bytes));
    }
}

internal static class InvoicePdfInspection
{
    public static string Read(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var content = new StringBuilder();
        foreach (PdfSharp.Pdf.PdfPage page in document.Pages)
        {
            content.Append(Encoding.Latin1.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue));
        }

        var text = new StringBuilder();
        var raw = content.ToString();
        for (var index = 0; index < raw.Length; index++)
        {
            if (raw[index] == '(')
            {
                index++;
                while (index < raw.Length && raw[index] != ')')
                {
                    if (raw[index] == '\\' && index + 1 < raw.Length)
                    {
                        index++;
                    }

                    text.Append(raw[index]);
                    index++;
                }
            }
            else if (raw[index] == '<')
            {
                var end = raw.IndexOf('>', index + 1);
                if (end <= index)
                {
                    break;
                }

                var hex = raw[(index + 1)..end];
                if (hex.Length >= 4 && hex.All(static character => character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'))
                {
                    var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : hex[..^1]);
                    text.Append(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF
                        ? Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2)
                        : Encoding.BigEndianUnicode.GetString(bytes));
                }

                index = end;
            }
        }

        return text.ToString();
    }
}
