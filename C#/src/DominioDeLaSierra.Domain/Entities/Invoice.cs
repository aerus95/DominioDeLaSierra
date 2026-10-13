using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class Invoice
{
    public const int MaxNumber = 999_999;
    public const int FullNumberMaxLength = 12;
    public const string TestBanner = "DOCUMENTO DE PRUEBA - SIN VALIDEZ FISCAL";
    public const string FictionalBanner = TestBanner;
    public const string MissingBuyerTaxIdLabel = "NIF no facilitado";
    public const string UndeterminedShippingVatLabel = "IVA no determinado";
    public const string MissingTaxIdClarification = "NIF no facilitado. La ausencia de NIF no convierte este documento en factura simplificada.";
    public const string UndeterminedShippingClarification = "El IVA del transporte no está determinado. No se ha aplicado ningún tipo.";

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public string Series { get; private set; } = null!;
    public int Number { get; private set; }
    public string FullNumber { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public bool Fictional { get; private set; }
    public FiscalDocumentForm FiscalForm { get; private set; }
    public string SellerLegalName { get; private set; } = null!;
    public string SellerTaxId { get; private set; } = null!;
    public string SellerAddressLine { get; private set; } = null!;
    public string SellerPostalCode { get; private set; } = null!;
    public string SellerCity { get; private set; } = null!;
    public string SellerProvince { get; private set; } = null!;
    public string SellerCountryCode { get; private set; } = null!;
    public string BuyerName { get; private set; } = null!;
    public string? BuyerTaxId { get; private set; }
    public string BuyerAddressLine { get; private set; } = null!;
    public string BuyerPostalCode { get; private set; } = null!;
    public string BuyerCity { get; private set; } = null!;
    public string BuyerProvince { get; private set; } = null!;
    public string BuyerCountryCode { get; private set; } = null!;
    public long ProductSubtotalCents { get; private set; }
    public long ProductTaxableBaseCents { get; private set; }
    public long ProductVatCents { get; private set; }
    public long ShippingCents { get; private set; }
    public decimal? ShippingVatRate { get; private set; }
    public long? ShippingTaxableBaseCents { get; private set; }
    public long? ShippingVatCents { get; private set; }
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

    public static Invoice IssueFiscal(
        Guid id,
        Guid orderId,
        string orderNumber,
        string series,
        int number,
        DateTimeOffset issuedAt,
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
        if (TestDocumentIssuer.IsReservedTaxId(seller.TaxId) || seller.LegalName.Contains("ficticio", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("El emisor ficticio no puede firmar una factura fiscal.");
        }

        var form = FiscalDocumentFormRules.Classify(testDocument: false, hasBuyerTaxId: true);
        var (shippingBase, shippingVat) = OrderAmounts.SplitGross(shippingCents, shippingVatRate);
        if (shippingBase != shippingTaxableBaseCents || shippingVat != shippingVatCents)
        {
            throw new ArgumentException("El IVA del transporte no cuadra.");
        }

        return Compose(
            id,
            orderId,
            orderNumber,
            series,
            number,
            issuedAt,
            fictional: false,
            form,
            seller,
            buyer.LegalName,
            buyer.TaxId,
            buyer.AddressLine,
            buyer.PostalCode,
            buyer.City,
            buyer.Province,
            buyer.CountryCode,
            drafts,
            productSubtotalCents,
            productTaxableBaseCents,
            productVatCents,
            shippingCents,
            shippingVatRate,
            shippingTaxableBaseCents,
            shippingVatCents,
            totalCents);
    }

    public static Invoice IssueTest(
        Guid id,
        Guid orderId,
        string orderNumber,
        string series,
        int number,
        DateTimeOffset issuedAt,
        InvoiceParty seller,
        string buyerName,
        string? buyerTaxId,
        string buyerAddressLine,
        string buyerPostalCode,
        string buyerCity,
        string buyerProvince,
        string buyerCountryCode,
        IReadOnlyList<InvoiceLineDraft> drafts,
        long productSubtotalCents,
        long productTaxableBaseCents,
        long productVatCents,
        long shippingCents,
        decimal? shippingVatRate,
        long? shippingTaxableBaseCents,
        long? shippingVatCents,
        long totalCents)
    {
        if (!TestDocumentIssuer.IsFictional(seller))
        {
            throw new ArgumentException("El emisor de un documento de prueba tiene que ser ficticio.");
        }

        if (!FiscalIdentity.TryContact(
                buyerName,
                buyerAddressLine,
                buyerPostalCode,
                buyerCity,
                buyerProvince,
                buyerCountryCode))
        {
            throw new ArgumentException("Los datos del comprador no están completos.");
        }

        string? taxId = null;
        if (!string.IsNullOrWhiteSpace(buyerTaxId))
        {
            taxId = FiscalIdentity.RequireTaxId(buyerTaxId);
        }

        decimal? rate = null;
        long? shippingBase = null;
        long? shippingVat = null;
        var shippingKnown = shippingVatRate is not null
            || shippingTaxableBaseCents is not null
            || shippingVatCents is not null;
        if (shippingKnown)
        {
            if (shippingVatRate is null || shippingTaxableBaseCents is null || shippingVatCents is null)
            {
                throw new ArgumentException("El IVA del transporte no cuadra.");
            }

            var split = OrderAmounts.SplitGross(shippingCents, shippingVatRate.Value);
            if (split.TaxableBaseCents != shippingTaxableBaseCents || split.VatCents != shippingVatCents)
            {
                throw new ArgumentException("El IVA del transporte no cuadra.");
            }

            rate = shippingVatRate;
            shippingBase = shippingTaxableBaseCents;
            shippingVat = shippingVatCents;
        }

        return Compose(
            id,
            orderId,
            orderNumber,
            series,
            number,
            issuedAt,
            fictional: true,
            FiscalDocumentFormRules.Classify(testDocument: true, hasBuyerTaxId: taxId is not null),
            seller,
            buyerName.Trim(),
            taxId,
            buyerAddressLine.Trim(),
            buyerPostalCode.Trim(),
            buyerCity.Trim(),
            buyerProvince.Trim(),
            buyerCountryCode.Trim().ToUpperInvariant(),
            drafts,
            productSubtotalCents,
            productTaxableBaseCents,
            productVatCents,
            shippingCents,
            rate,
            shippingBase,
            shippingVat,
            totalCents);
    }

    private static Invoice Compose(
        Guid id,
        Guid orderId,
        string orderNumber,
        string series,
        int number,
        DateTimeOffset issuedAt,
        bool fictional,
        FiscalDocumentForm form,
        InvoiceParty seller,
        string buyerName,
        string? buyerTaxId,
        string buyerAddressLine,
        string buyerPostalCode,
        string buyerCity,
        string buyerProvince,
        string buyerCountryCode,
        IReadOnlyList<InvoiceLineDraft> drafts,
        long productSubtotalCents,
        long productTaxableBaseCents,
        long productVatCents,
        long shippingCents,
        decimal? shippingVatRate,
        long? shippingTaxableBaseCents,
        long? shippingVatCents,
        long totalCents)
    {
        OrderText.RequireId(id, "La factura");
        OrderText.RequireId(orderId, "El pedido");
        if (drafts is null || drafts.Count == 0)
        {
            throw new ArgumentException("La factura debe incluir al menos una línea.");
        }

        if (form == FiscalDocumentForm.Simplified)
        {
            throw new ArgumentException("La factura simplificada no está habilitada.");
        }

        if (fictional != (form == FiscalDocumentForm.NotApplicable))
        {
            throw new ArgumentException("El documento de prueba no puede usar una clase de factura fiscal.");
        }

        if (!fictional && string.IsNullOrWhiteSpace(buyerTaxId))
        {
            throw new ArgumentException("La factura fiscal necesita el NIF del comprador.");
        }

        if (!fictional && shippingVatRate is null)
        {
            throw new ArgumentException("El IVA del transporte no está determinado.");
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
            FiscalForm = form,
            SellerLegalName = seller.LegalName,
            SellerTaxId = seller.TaxId,
            SellerAddressLine = seller.AddressLine,
            SellerPostalCode = seller.PostalCode,
            SellerCity = seller.City,
            SellerProvince = seller.Province,
            SellerCountryCode = seller.CountryCode,
            BuyerName = OrderText.Require(buyerName, FiscalIdentity.LegalNameMaxLength, "El comprador"),
            BuyerTaxId = buyerTaxId,
            BuyerAddressLine = OrderText.Require(buyerAddressLine, FiscalIdentity.AddressLineMaxLength, "La dirección"),
            BuyerPostalCode = OrderText.Require(buyerPostalCode, FiscalIdentity.PostalCodeMaxLength, "El código postal"),
            BuyerCity = OrderText.Require(buyerCity, FiscalIdentity.CityMaxLength, "La localidad"),
            BuyerProvince = OrderText.Require(buyerProvince, FiscalIdentity.ProvinceMaxLength, "La provincia"),
            BuyerCountryCode = OrderText.Require(buyerCountryCode, 2, "El país"),
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
