namespace DominioDeLaSierra.Domain;

public enum FiscalDocumentForm
{
    NotApplicable = 0,
    Complete = 1,
    Simplified = 2
}

public static class FiscalDocumentFormRules
{
    public const bool MissingTaxIdAllowsSimplified = false;
    public const string UndeterminedMessage = "No se puede determinar la clase de factura. La ausencia de NIF no autoriza una factura simplificada.";

    public static FiscalDocumentForm Classify(bool testDocument, bool hasBuyerTaxId)
    {
        if (testDocument)
        {
            return FiscalDocumentForm.NotApplicable;
        }

        if (!hasBuyerTaxId)
        {
            throw new InvalidOperationException(UndeterminedMessage);
        }

        return FiscalDocumentForm.Complete;
    }
}
