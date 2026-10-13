namespace DominioDeLaSierra.Domain;

public static class TestDocumentIssuer
{
    public const string LegalName = "Emisor ficticio de pruebas";
    public const string TaxId = "FICTICIO-TEST";
    public const string AddressLine = "Calle Ficticia 1";
    public const string PostalCode = "00000";
    public const string City = "Ciudad de prueba";
    public const string Province = "Provincia de prueba";
    public const string CountryCode = "ES";

    public static InvoiceParty Party { get; } = new(
        LegalName,
        TaxId,
        AddressLine,
        PostalCode,
        City,
        Province,
        CountryCode);

    public static bool IsReservedTaxId(string? taxId) =>
        string.Equals(taxId?.Trim(), TaxId, StringComparison.OrdinalIgnoreCase);

    public static bool IsFictional(InvoiceParty party)
    {
        return IsReservedTaxId(party.TaxId)
            && party.LegalName.Contains("ficticio", StringComparison.OrdinalIgnoreCase);
    }
}
