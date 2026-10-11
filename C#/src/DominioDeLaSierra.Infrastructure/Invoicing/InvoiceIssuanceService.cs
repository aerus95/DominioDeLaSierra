using System.Data;
using DominioDeLaSierra.Application.Invoicing;
using DominioDeLaSierra.Domain;
using DominioDeLaSierra.Domain.Entities;
using DominioDeLaSierra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DominioDeLaSierra.Infrastructure.Invoicing;

public sealed class InvoiceIssuanceService(
    ApplicationDbContext db,
    IOptions<InvoiceOptions> options,
    IHostEnvironment environment,
    InvoiceIssuanceFailureProbe failures) : IInvoiceIssuance
{
    public async Task<InvoicePanel?> DescribeAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var invoice = await FindAsync(orderId, cancellationToken);
        if (invoice is not null)
        {
            return Issued(invoice);
        }

        var order = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var notice = BlockReason(order);
        return notice is null
            ? new InvoicePanel(false, false, null, null, true, null, "Descargar factura")
            : Blocked(notice, order.Status == OrderStatus.Paid);
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
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Render(invoice);
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
        var order = await db.Orders.AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new InvoiceIssuanceException(InvoiceIssuanceRules.MissingOrderMessage);
        var notice = BlockReason(order);
        if (notice is not null)
        {
            throw new InvoiceIssuanceException(notice);
        }

        var current = options.Value;
        var series = FiscalIdentity.RequireSeries(current.Series);
        var seller = FiscalIdentity.RequireParty(
            current.Seller.LegalName,
            current.Seller.TaxId,
            current.Seller.AddressLine,
            current.Seller.PostalCode,
            current.Seller.City,
            current.Seller.Province,
            current.Seller.CountryCode);
        var buyer = FiscalIdentity.RequireParty(
            order.CustomerName,
            order.CustomerTaxId,
            order.AddressLine,
            order.PostalCode,
            order.City,
            order.Province,
            order.CountryCode);
        var number = await ReserveNumberAsync(series, current.FirstNumber, cancellationToken);
        if (failures.ConsumeFailure())
        {
            throw new InvalidOperationException("Fallo de prueba antes de emitir la factura.");
        }

        var shippingRate = order.ShippingVatRate
            ?? throw new InvoiceIssuanceException(InvoiceIssuanceRules.ShippingVatMessage);
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
            return Invoice.Issue(
                Guid.NewGuid(),
                order.Id,
                order.Number,
                series,
                number,
                DateTimeOffset.UtcNow,
                current.Fictional,
                seller,
                buyer,
                drafts,
                order.ProductSubtotalCents,
                order.ProductTaxableBaseCents,
                order.ProductVatCents,
                order.ShippingCents,
                shippingRate,
                order.ShippingTaxableBaseCents ?? -1,
                order.ShippingVatCents ?? -1,
                order.TotalCents);
        }
        catch (ArgumentException)
        {
            throw new InvoiceIssuanceException(InvoiceIssuanceRules.InconsistentMessage);
        }
    }

    private string? BlockReason(Order order)
    {
        if (order.Status != OrderStatus.Paid)
        {
            return InvoiceIssuanceRules.UnpaidMessage;
        }

        var current = options.Value;
        var testing = InvoiceIssuanceGuard.IsTesting(environment.EnvironmentName);
        if (!current.Enabled)
        {
            return InvoiceIssuanceRules.PendingConfigurationMessage;
        }

        if (current.Fictional && !testing)
        {
            return InvoiceIssuanceRules.FictionalOutsideMessage;
        }

        if (testing && !current.Fictional)
        {
            return InvoiceIssuanceRules.FictionalTestingMessage;
        }

        if (!current.IsSeriesReady())
        {
            return InvoiceIssuanceRules.PendingConfigurationMessage;
        }

        if (!current.IsSellerReady())
        {
            return InvoiceIssuanceRules.SellerMessage;
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
            return InvoiceIssuanceRules.BuyerMessage;
        }

        if (order.ShippingVatRate is null || order.ShippingTaxableBaseCents is null || order.ShippingVatCents is null)
        {
            return InvoiceIssuanceRules.ShippingVatMessage;
        }

        if (!AmountsMatch(order))
        {
            return InvoiceIssuanceRules.InconsistentMessage;
        }

        return null;
    }

    private static bool AmountsMatch(Order order)
    {
        if (order.Items.Count == 0
            || order.ProductTaxableBaseCents + order.ProductVatCents != order.ProductSubtotalCents
            || order.TotalCents != order.ProductSubtotalCents + order.ShippingCents
            || order.ShippingVatRate is null
            || order.ShippingTaxableBaseCents is null
            || order.ShippingVatCents is null)
        {
            return false;
        }

        try
        {
            var (shippingBase, shippingVat) = OrderAmounts.SplitGross(order.ShippingCents, order.ShippingVatRate.Value);
            if (shippingBase != order.ShippingTaxableBaseCents || shippingVat != order.ShippingVatCents)
            {
                return false;
            }

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

    private static InvoicePanel Blocked(string notice, bool showAction) =>
        new(false, false, null, null, false, notice, showAction ? "Descargar factura" : string.Empty);

    private static InvoiceFile Render(Invoice invoice)
    {
        return new InvoiceFile(
            InvoicePdf.Render(invoice),
            invoice.DownloadFileName,
            invoice.FullNumber,
            invoice.IssuedAt,
            invoice.Fictional,
            invoice.TotalCents);
    }

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
}
