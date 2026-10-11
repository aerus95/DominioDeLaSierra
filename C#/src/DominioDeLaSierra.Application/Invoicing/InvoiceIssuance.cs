using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Application.Invoicing;

public sealed class InvoiceOptions
{
    public const string SectionName = "InvoiceIssuance";

    public bool Enabled { get; set; }

    public bool Fictional { get; set; }

    public string Series { get; set; } = string.Empty;

    public int FirstNumber { get; set; } = 1;

    public InvoiceSellerOptions Seller { get; set; } = new();

    public bool IsSellerReady()
    {
        var seller = Seller ?? new InvoiceSellerOptions();
        return FiscalIdentity.TryParty(
            seller.LegalName,
            seller.TaxId,
            seller.AddressLine,
            seller.PostalCode,
            seller.City,
            seller.Province,
            seller.CountryCode,
            out _);
    }

    public bool IsSeriesReady()
    {
        return FiscalIdentity.TrySeries(Series, out _)
            && FirstNumber >= 1
            && FirstNumber <= Domain.Entities.Invoice.MaxNumber;
    }
}

public sealed class InvoiceSellerOptions
{
    public string LegalName { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    public string CountryCode { get; set; } = string.Empty;
}

public static class InvoiceIssuanceRules
{
    public const string PendingConfigurationMessage = "Facturación pendiente de configuración fiscal.";
    public const string SellerMessage = "Faltan datos fiscales del emisor.";
    public const string BuyerMessage = "El pedido no tiene los datos fiscales del comprador.";
    public const string ShippingVatMessage = "El tratamiento del IVA del transporte no está determinado.";
    public const string UnpaidMessage = "Solo se puede facturar un pedido pagado.";
    public const string InconsistentMessage = "Los importes del pedido no cuadran.";
    public const string MissingOrderMessage = "El pedido no existe.";
    public const string FictionalTestingMessage = "Las pruebas no pueden emitir facturas fiscales.";
    public const string FictionalOutsideMessage = "Las facturas ficticias solo se permiten en las pruebas.";
    public const string SeriesExhaustedMessage = "La serie de facturación no admite más números.";
}

public sealed class InvoiceIssuanceException : Exception
{
    public InvoiceIssuanceException(string message) : base(message)
    {
    }
}

public static class InvoiceIssuanceGuard
{
    public static void Ensure(string environmentName, InvoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var testing = IsTesting(environmentName);
        if (options.Fictional && !testing)
        {
            throw new InvalidOperationException(InvoiceIssuanceRules.FictionalOutsideMessage);
        }

        if (testing && options.Enabled && !options.Fictional)
        {
            throw new InvalidOperationException(InvoiceIssuanceRules.FictionalTestingMessage);
        }
    }

    public static bool IsTesting(string environmentName) =>
        string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
}

public sealed record InvoicePanel(
    bool Issued,
    bool Fictional,
    string? FullNumber,
    DateTimeOffset? IssuedAt,
    bool CanIssue,
    string? Notice,
    string ActionLabel);

public sealed record InvoiceFile(
    byte[] Content,
    string FileName,
    string FullNumber,
    DateTimeOffset IssuedAt,
    bool Fictional,
    long TotalCents);

public interface IInvoiceIssuance
{
    Task<InvoicePanel?> DescribeAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<InvoiceFile> DownloadAsync(Guid orderId, CancellationToken cancellationToken = default);
}
