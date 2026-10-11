using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Invoice
{
    public const int MaxNumber = 999_999;
    public const int FullNumberMaxLength = 12;
    public const string FictionalBanner = "DOCUMENTO FICTICIO DE PRUEBAS. NO ES UNA FACTURA FISCAL.";

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public string Series { get; private set; } = null!;
    public int Number { get; private set; }
    public string FullNumber { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public bool Fictional { get; private set; }
    public string SellerLegalName { get; private set; } = null!;
    public string SellerTaxId { get; private set; } = null!;
    public string SellerAddressLine { get; private set; } = null!;
    public string SellerPostalCode { get; private set; } = null!;
    public string SellerCity { get; private set; } = null!;
    public string SellerProvince { get; private set; } = null!;
    public string SellerCountryCode { get; private set; } = null!;
    public string BuyerName { get; private set; } = null!;
    public string BuyerTaxId { get; private set; } = null!;
    public string BuyerAddressLine { get; private set; } = null!;
    public string BuyerPostalCode { get; private set; } = null!;
    public string BuyerCity { get; private set; } = null!;
    public string BuyerProvince { get; private set; } = null!;
    public string BuyerCountryCode { get; private set; } = null!;
    public long ProductSubtotalCents { get; private set; }
    public long ProductTaxableBaseCents { get; private set; }
    public long ProductVatCents { get; private set; }
    public long ShippingCents { get; private set; }
    public decimal ShippingVatRate { get; private set; }
    public long ShippingTaxableBaseCents { get; private set; }
    public long ShippingVatCents { get; private set; }
    public long TotalCents { get; private set; }
    public string Currency { get; private set; } = OrderAmounts.Currency;

    public IReadOnlyList<InvoiceLine> Lines => lines;

    public string DownloadFileName => Fictional ? $"ficticio-{FullNumber}.pdf" : $"factura-{FullNumber}.pdf";

    private readonly List<InvoiceLine> lines = [];

    private Invoice()
    {
    }

    public static string FormatNumber(string series, int number)
    {
        if (number < 1 || number > MaxNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "El número de factura no es válido.");
        }

        return $"{FiscalIdentity.RequireSeries(series)}-{number:D6}";
    }

    public static Invoice Issue(
        Guid id,
        Guid orderId,
        string orderNumber,
        string series,
        int number,
        DateTimeOffset issuedAt,
        bool fictional,
        InvoiceParty seller,
        InvoiceParty buyer,
        IReadOnlyList<InvoiceLineDraft> drafts,
        long productSubtotalCents,
        long productTaxableBaseCents,
        long productVatCents,
        long shippingCents,
        decimal shippingVatRate,
        long shippingTaxableBaseCents,
        long shippingVatCents,
        long totalCents)
    {
        OrderText.RequireId(id, "La factura");
        OrderText.RequireId(orderId, "El pedido");
        if (drafts is null || drafts.Count == 0)
        {
            throw new ArgumentException("La factura debe incluir al menos una línea.");
        }

        var (shippingBase, shippingVat) = OrderAmounts.SplitGross(shippingCents, shippingVatRate);
        if (shippingBase != shippingTaxableBaseCents || shippingVat != shippingVatCents)
        {
            throw new ArgumentException("El IVA del transporte no cuadra.");
        }

        if (productSubtotalCents < 0
            || productTaxableBaseCents < 0
            || productVatCents < 0
            || productTaxableBaseCents + productVatCents != productSubtotalCents
            || totalCents != productSubtotalCents + shippingCents)
        {
            throw new ArgumentException("Los importes de la factura no cuadran.");
        }

        var invoice = new Invoice
        {
            Id = id,
            OrderId = orderId,
            OrderNumber = OrderText.RequireToken(orderNumber, Order.NumberMaxLength, "El número de pedido"),
            Series = FiscalIdentity.RequireSeries(series),
            Number = number,
            FullNumber = FormatNumber(series, number),
            IssuedAt = issuedAt,
            Fictional = fictional,
            SellerLegalName = seller.LegalName,
            SellerTaxId = seller.TaxId,
            SellerAddressLine = seller.AddressLine,
            SellerPostalCode = seller.PostalCode,
            SellerCity = seller.City,
            SellerProvince = seller.Province,
            SellerCountryCode = seller.CountryCode,
            BuyerName = buyer.LegalName,
            BuyerTaxId = buyer.TaxId,
            BuyerAddressLine = buyer.AddressLine,
            BuyerPostalCode = buyer.PostalCode,
            BuyerCity = buyer.City,
            BuyerProvince = buyer.Province,
            BuyerCountryCode = buyer.CountryCode,
            ProductSubtotalCents = productSubtotalCents,
            ProductTaxableBaseCents = productTaxableBaseCents,
            ProductVatCents = productVatCents,
            ShippingCents = shippingCents,
            ShippingVatRate = shippingVatRate,
            ShippingTaxableBaseCents = shippingTaxableBaseCents,
            ShippingVatCents = shippingVatCents,
            TotalCents = totalCents,
            Currency = OrderAmounts.Currency
        };

        long subtotal = 0;
        long taxableBase = 0;
        long vat = 0;
        for (var index = 0; index < drafts.Count; index++)
        {
            var line = InvoiceLine.Create(invoice, index + 1, drafts[index]);
            invoice.lines.Add(line);
            subtotal = checked(subtotal + line.LineTotalCents);
            taxableBase = checked(taxableBase + line.TaxableBaseCents);
            vat = checked(vat + line.VatCents);
        }

        if (subtotal != productSubtotalCents || taxableBase != productTaxableBaseCents || vat != productVatCents)
        {
            throw new ArgumentException("Los importes de la factura no cuadran.");
        }

        return invoice;
    }
}
