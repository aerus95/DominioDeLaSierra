using System.Data;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Invoicing;

public sealed class InvoiceIssuanceService(
    ApplicationDbContext db,
    IOptions<InvoiceOptions> options,
    IConfiguration configuration,
    InvoiceIssuanceFailureProbe failures) : IInvoiceIssuance
{
    public async Task<InvoicePanel?> DescribeAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindAsync(orderId, cancellationToken);
        if (invoice is not null)
        {
            return Issued(invoice);
        }

        var order = await Orders()
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var decision = Decide(order);
        if (decision.Kind == IssueKind.Blocked)
        {
            var paid = order.Status == OrderStatus.Paid;
            var label = paid
                ? PlatformIsTest() ? "Descargar documento de prueba" : "Descargar factura"
                : string.Empty;
            return new InvoicePanel(false, false, null, null, false, decision.Notice, label);
        }

        return decision.Kind == IssueKind.Test
            ? new InvoicePanel(false, true, null, null, true, Invoice.TestBanner, "Descargar documento de prueba")
            : new InvoicePanel(false, false, null, null, true, null, "Descargar factura");
    }

    public async Task<InvoiceFile> DownloadAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var existing = await FindAsync(orderId, cancellationToken);
        if (existing is not null)
        {
            return Render(existing);
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await LockOrderAsync(orderId, cancellationToken))
            {
                throw new InvoiceIssuanceException(InvoiceIssuanceRules.MissingOrderMessage);
            }

            existing = await FindAsync(orderId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Render(existing);
            }

            var invoice = await CreateAsync(orderId, cancellationToken);
            byte[] content;
            try
            {
                content = InvoicePdf.Render(invoice);
            }
            catch (InvalidOperationException)
            {
                throw new InvoiceIssuanceException(InvoiceIssuanceRules.PdfMessage);
            }

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return File(invoice, content);
        }
        catch (DbUpdateException exception) when (IsOrderConflict(exception))
        {
            db.ChangeTracker.Clear();
            var winner = await FindAsync(orderId, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Render(winner);
        }
    }

    private async Task<Invoice> CreateAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await Orders()
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new InvoiceIssuanceException(InvoiceIssuanceRules.MissingOrderMessage);
        var decision = Decide(order);
        if (decision.Kind == IssueKind.Blocked)
        {
            throw new InvoiceIssuanceException(decision.Notice!);
        }

        var current = options.Value;
        var series = FiscalIdentity.RequireSeries(current.Series);
        var number = await ReserveNumberAsync(series, current.FirstNumber, cancellationToken);
        if (failures.ConsumeFailure())
        {
            throw new InvalidOperationException("Fallo de prueba antes de emitir la factura.");
        }

        var drafts = order.Items.Select(item => new InvoiceLineDraft(
            item.Name,
            item.Reference,
            item.Quantity,
            item.UnitPriceCents,
            item.VatRate,
            item.LineTotalCents,
            item.TaxableBaseCents,
            item.VatCents)).ToList();
        try
        {
            return decision.Kind == IssueKind.Test
                ? Invoice.IssueTest(
                    Guid.NewGuid(),
                    order.Id,
                    order.Number,
                    series,
                    number,
                    DateTimeOffset.UtcNow,
                    TestDocumentIssuer.Party,
                    order.CustomerName,
                    order.CustomerTaxId,
                    order.AddressLine,
                    order.PostalCode,
                    order.City,
                    order.Province,
                    order.CountryCode,
                    drafts,
                    order.ProductSubtotalCents,
                    order.ProductTaxableBaseCents,
                    order.ProductVatCents,
                    order.ShippingCents,
                    order.ShippingVatRate,
                    order.ShippingTaxableBaseCents,
                    order.ShippingVatCents,
                    order.TotalCents)
                : Invoice.IssueFiscal(
                    Guid.NewGuid(),
                    order.Id,
                    order.Number,
                    series,
                    number,
                    DateTimeOffset.UtcNow,
                    ConfirmedSeller(),
                    Buyer(order),
                    drafts,
                    order.ProductSubtotalCents,
                    order.ProductTaxableBaseCents,
                    order.ProductVatCents,
                    order.ShippingCents,
                    order.ShippingVatRate!.Value,
                    order.ShippingTaxableBaseCents!.Value,
                    order.ShippingVatCents!.Value,
                    order.TotalCents);
        }
        catch (ArgumentException)
        {
            throw new InvoiceIssuanceException(InvoiceIssuanceRules.InconsistentMessage);
        }
        catch (InvalidOperationException exception) when (exception.Message == FiscalDocumentFormRules.UndeterminedMessage)
        {
            throw new InvoiceIssuanceException(exception.Message);
        }
    }

    private IssueDecision Decide(Order order)
    {
        if (order.Status != OrderStatus.Paid)
        {
            return IssueDecision.Block(InvoiceIssuanceRules.UnpaidMessage);
        }

        if (order.Payments.Any(payment => payment.Provider != PaymentProvider.Stripe))
        {
            return IssueDecision.Block(InvoiceIssuanceRules.UnspecifiedProviderMessage);
        }

        var current = options.Value;
        if (!current.Enabled)
        {
            return IssueDecision.Block(InvoiceIssuanceRules.PendingConfigurationMessage);
        }

        if (!current.IsSeriesReady())
        {
            return IssueDecision.Block(InvoiceIssuanceRules.PendingConfigurationMessage);
        }

        if (!ProductAmountsMatch(order) || !ShippingAmountsMatch(order))
        {
            return IssueDecision.Block(InvoiceIssuanceRules.InconsistentMessage);
        }

        var platformTest = PlatformIsTest();
        var testSession = HasSessionPrefix(order, "cs_test_");
        var liveSession = HasSessionPrefix(order, "cs_live_");
        if (platformTest && liveSession)
        {
            return IssueDecision.Block(InvoiceIssuanceRules.MixedStripeMessage);
        }

        if (platformTest || testSession)
        {
            if (!FiscalIdentity.TryContact(
                    order.CustomerName,
                    order.AddressLine,
                    order.PostalCode,
                    order.City,
                    order.Province,
                    order.CountryCode))
            {
                return IssueDecision.Block(InvoiceIssuanceRules.BuyerMessage);
            }

            return IssueDecision.Allow(IssueKind.Test);
        }

        if (!liveSession)
        {
            return IssueDecision.Block(InvoiceIssuanceRules.LivePaymentRequiredMessage);
        }

        if (!current.IsSellerReady())
        {
            return IssueDecision.Block(InvoiceIssuanceRules.SellerMessage);
        }

        var seller = SellerParty(current);
        if (TestDocumentIssuer.IsReservedTaxId(seller.TaxId)
            || seller.LegalName.Contains("ficticio", StringComparison.OrdinalIgnoreCase))
        {
            return IssueDecision.Block(InvoiceIssuanceRules.FictionalSellerMessage);
        }

        if (string.IsNullOrWhiteSpace(order.CustomerTaxId))
        {
            return IssueDecision.Block(InvoiceIssuanceRules.InvoiceFormMessage);
        }

        if (!FiscalIdentity.TryParty(
                order.CustomerName,
                order.CustomerTaxId,
                order.AddressLine,
                order.PostalCode,
                order.City,
                order.Province,
                order.CountryCode,
                out _))
        {
            return IssueDecision.Block(InvoiceIssuanceRules.BuyerMessage);
        }

        if (order.ShippingVatRate is null || order.ShippingTaxableBaseCents is null || order.ShippingVatCents is null)
        {
            return IssueDecision.Block(InvoiceIssuanceRules.ShippingVatMessage);
        }

        return IssueDecision.Allow(IssueKind.Fiscal);
    }

    private bool PlatformIsTest() =>
        InvoiceStripeMode.IsTest(
            configuration["Stripe:Gateway"],
            configuration["Stripe:SecretKey"] ?? configuration["STRIPE_SECRET_KEY"]);

    private static bool HasSessionPrefix(Order order, string prefix) =>
        order.Payments.Any(payment =>
            payment.StripeCheckoutSessionId?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);

    private static bool ProductAmountsMatch(Order order)
    {
        if (order.Items.Count == 0
            || order.ProductTaxableBaseCents + order.ProductVatCents != order.ProductSubtotalCents
            || order.TotalCents != order.ProductSubtotalCents + order.ShippingCents)
        {
            return false;
        }

        try
        {
            long subtotal = 0;
            long taxableBase = 0;
            long vat = 0;
            foreach (var item in order.Items)
            {
                if (OrderAmounts.Multiply(item.UnitPriceCents, item.Quantity) != item.LineTotalCents
                    || item.TaxableBaseCents + item.VatCents != item.LineTotalCents)
                {
                    return false;
                }

                var (lineBase, lineVat) = OrderAmounts.SplitGross(item.LineTotalCents, item.VatRate);
                if (lineBase != item.TaxableBaseCents || lineVat != item.VatCents)
                {
                    return false;
                }

                subtotal += item.LineTotalCents;
                taxableBase += item.TaxableBaseCents;
                vat += item.VatCents;
            }

            return subtotal == order.ProductSubtotalCents
                && taxableBase == order.ProductTaxableBaseCents
                && vat == order.ProductVatCents;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ShippingAmountsMatch(Order order)
    {
        if (order.ShippingVatRate is null && order.ShippingTaxableBaseCents is null && order.ShippingVatCents is null)
        {
            return true;
        }

        if (order.ShippingVatRate is null || order.ShippingTaxableBaseCents is null || order.ShippingVatCents is null)
        {
            return false;
        }

        try
        {
            var (shippingBase, shippingVat) = OrderAmounts.SplitGross(order.ShippingCents, order.ShippingVatRate.Value);
            return shippingBase == order.ShippingTaxableBaseCents && shippingVat == order.ShippingVatCents;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private InvoiceParty ConfirmedSeller()
    {
        var seller = options.Value.Seller;
        return FiscalIdentity.RequireParty(
            seller.LegalName,
            seller.TaxId,
            seller.AddressLine,
            seller.PostalCode,
            seller.City,
            seller.Province,
            seller.CountryCode);
    }

    private static InvoiceParty SellerParty(InvoiceOptions current) =>
        new(
            current.Seller.LegalName.Trim(),
            current.Seller.TaxId.Trim().ToUpperInvariant(),
            current.Seller.AddressLine.Trim(),
            current.Seller.PostalCode.Trim(),
            current.Seller.City.Trim(),
            current.Seller.Province.Trim(),
            current.Seller.CountryCode.Trim().ToUpperInvariant());

    private static InvoiceParty Buyer(Order order) =>
        FiscalIdentity.RequireParty(
            order.CustomerName,
            order.CustomerTaxId,
            order.AddressLine,
            order.PostalCode,
            order.City,
            order.Province,
            order.CountryCode);

    private IQueryable<Order> Orders() =>
        db.Orders.AsNoTracking()
            .Include(order => order.Items)
            .Include(order => order.Payments);

    private async Task<int> ReserveNumberAsync(string series, int firstNumber, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException(InvoiceIssuanceRules.PendingConfigurationMessage);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO "InvoiceSeries" ("Code", "NextNumber")
                VALUES (@code, @first)
                ON CONFLICT ("Code") DO NOTHING
                """;
            Add(insert, "code", series);
            Add(insert, "first", firstNumber);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        int number;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """SELECT "NextNumber" FROM "InvoiceSeries" WHERE "Code" = @code FOR UPDATE""";
            Add(select, "code", series);
            var raw = await select.ExecuteScalarAsync(cancellationToken);
            number = raw is int value ? value : Convert.ToInt32(raw);
        }

        if (number < 1 || number > Invoice.MaxNumber)
        {
            throw new InvoiceIssuanceException(InvoiceIssuanceRules.SeriesExhaustedMessage);
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """UPDATE "InvoiceSeries" SET "NextNumber" = "NextNumber" + 1 WHERE "Code" = @code""";
            Add(update, "code", series);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return number;
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task<bool> LockOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException(InvoiceIssuanceRules.PendingConfigurationMessage);
        command.CommandText = """SELECT "Id" FROM "Orders" WHERE "Id" = @id FOR UPDATE""";
        Add(command, "id", orderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken);
    }

    private Task<Invoice?> FindAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Invoices.AsNoTracking()
            .Include(invoice => invoice.Lines)
            .SingleOrDefaultAsync(invoice => invoice.OrderId == orderId, cancellationToken);

    private static InvoicePanel Issued(Invoice invoice) =>
        new(
            true,
            invoice.Fictional,
            invoice.FullNumber,
            invoice.IssuedAt,
            true,
            invoice.Fictional ? Invoice.FictionalBanner : null,
            invoice.Fictional ? "Descargar documento de prueba" : "Descargar factura");

    private static InvoiceFile Render(Invoice invoice)
    {
        return File(invoice, InvoicePdf.Render(invoice));
    }

    private static InvoiceFile File(Invoice invoice, byte[] content) =>
        new(
            content,
            invoice.DownloadFileName,
            invoice.FullNumber,
            invoice.IssuedAt,
            invoice.Fictional,
            invoice.TotalCents);

    private static bool IsOrderConflict(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(postgres.ConstraintName, "IX_Invoices_OrderId", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private enum IssueKind
    {
        Test,
        Fiscal,
        Blocked
    }

    private readonly record struct IssueDecision(IssueKind Kind, string? Notice)
    {
        public static IssueDecision Block(string notice) => new(IssueKind.Blocked, notice);

        public static IssueDecision Allow(IssueKind kind) => new(kind, null);
    }
}
