namespace DominioDeLaSierra.Domain;

public static class FiscalIdentity
{
    public const int TaxIdMaxLength = 20;
    public const int LegalNameMaxLength = 160;
    public const int AddressLineMaxLength = 200;
    public const int PostalCodeMaxLength = 10;
    public const int CityMaxLength = 120;
    public const int ProvinceMaxLength = 120;

    public static string RequireTaxId(string? value)
    {
        if (!TryTaxId(value, out var taxId))
        {
            throw new ArgumentException("El NIF no es válido.");
        }

        return taxId;
    }

    public static bool TryTaxId(string? value, out string taxId)
    {
        taxId = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (taxId.Length is < 5 or > TaxIdMaxLength)
        {
            return false;
        }

        foreach (var character in taxId)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public static string RequireSeries(string? value)
    {
        if (!TrySeries(value, out var series))
        {
            throw new ArgumentException("La serie de facturación no es válida.");
        }

        return series;
    }

    public static bool TrySeries(string? value, out string series)
    {
        series = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (series.Length != 5 || series[0] != 'F')
        {
            return false;
        }

        for (var index = 1; index < series.Length; index++)
        {
            if (!char.IsDigit(series[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static InvoiceParty RequireParty(
        string? legalName,
        string? taxId,
        string? addressLine,
        string? postalCode,
        string? city,
        string? province,
        string? countryCode)
    {
        if (!TryParty(legalName, taxId, addressLine, postalCode, city, province, countryCode, out var party))
        {
            throw new ArgumentException("Los datos fiscales no están completos.");
        }

        return party;
    }

    public static bool TryParty(
        string? legalName,
        string? taxId,
        string? addressLine,
        string? postalCode,
        string? city,
        string? province,
        string? countryCode,
        out InvoiceParty party)
    {
        party = null!;
        if (!TryText(legalName, LegalNameMaxLength, out var name)
            || !TryTaxId(taxId, out var id)
            || !TryText(addressLine, AddressLineMaxLength, out var address)
            || !TryText(postalCode, PostalCodeMaxLength, out var postal)
            || !TryText(city, CityMaxLength, out var locality)
            || !TryText(province, ProvinceMaxLength, out var region)
            || !TryCountry(countryCode, out var country))
        {
            return false;
        }

        party = new InvoiceParty(name, id, address, postal, locality, region, country);
        return true;
    }

    private static bool TryCountry(string? value, out string country)
    {
        country = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return country.Length == 2 && country[0] is >= 'A' and <= 'Z' && country[1] is >= 'A' and <= 'Z';
    }

    private static bool TryText(string? value, int maxLength, out string text)
    {
        text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > maxLength)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                text = string.Empty;
                return false;
            }
        }

        return true;
    }
}

public sealed record InvoiceParty(
    string LegalName,
    string TaxId,
    string AddressLine,
    string PostalCode,
    string City,
    string Province,
    string CountryCode);
