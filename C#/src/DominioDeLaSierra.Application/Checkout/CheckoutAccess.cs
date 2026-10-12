namespace DominioDeLaSierra.Application.Checkout;

public static class CheckoutAccessRules
{
    public const string SectionName = "Checkout";
    public const string Closed = "Closed";
    public const string Testers = "Testers";
    public const string Public = "Public";
    public const string UnavailableMessage = "La compra online estará disponible próximamente";
    public const string PublicProductionMessage = "El checkout público no está habilitado.";
    public const string InvalidModeMessage = "El modo de acceso al checkout no es válido.";

    public static string? Normalize(string? mode)
    {
        var value = mode?.Trim() ?? string.Empty;
        if (value.Length == 0
            || string.Equals(value, Closed, StringComparison.OrdinalIgnoreCase))
        {
            return Closed;
        }

        if (string.Equals(value, Testers, StringComparison.OrdinalIgnoreCase))
        {
            return Testers;
        }

        if (string.Equals(value, Public, StringComparison.OrdinalIgnoreCase))
        {
            return Public;
        }

        return null;
    }

    public static void Ensure(string environmentName, string? mode)
    {
        var normalized = Normalize(mode) ?? throw new InvalidOperationException(InvalidModeMessage);
        if (IsProduction(environmentName) && normalized == Public)
        {
            throw new InvalidOperationException(PublicProductionMessage);
        }
    }

    public static bool Allows(string? mode, bool authenticatedTester)
    {
        return Normalize(mode) switch
        {
            Public => true,
            Testers => authenticatedTester,
            _ => false
        };
    }

    public static bool IsProduction(string environmentName) =>
        !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);
}

public sealed record CheckoutAvailabilityDto(bool Allowed);
