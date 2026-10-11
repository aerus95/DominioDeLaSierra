using DominioDeLaSierra.Domain;

namespace DominioDeLaSierra.Domain.Entities;

public sealed class InvoiceLine
{
    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public int Position { get; private set; }
    public string Name { get; private set; } = null!;
    public string Reference { get; private set; } = null!;
    public int Quantity { get; private set; }
    public long UnitPriceCents { get; private set; }
    public decimal VatRate { get; private set; }
    public long LineTotalCents { get; private set; }
    public long TaxableBaseCents { get; private set; }
    public long VatCents { get; private set; }

    public Invoice Invoice { get; private set; } = null!;

    private InvoiceLine()
    {
    }

    internal static InvoiceLine Create(Invoice invoice, int position, InvoiceLineDraft draft)
    {
        var lineTotal = OrderAmounts.Multiply(draft.UnitPriceCents, draft.Quantity);
        if (lineTotal != draft.LineTotalCents
            || draft.TaxableBaseCents < 0
            || draft.VatCents < 0
            || draft.TaxableBaseCents + draft.VatCents != draft.LineTotalCents)
        {
            throw new ArgumentException("El importe de la línea no cuadra.");
        }

        var (baseCents, vatCents) = OrderAmounts.SplitGross(draft.LineTotalCents, draft.VatRate);
        if (baseCents != draft.TaxableBaseCents || vatCents != draft.VatCents)
        {
            throw new ArgumentException("El IVA de la línea no cuadra.");
        }

        return new InvoiceLine
        {
            Id = Guid.NewGuid(),
            Invoice = invoice,
            InvoiceId = invoice.Id,
            Position = position,
            Name = OrderText.Require(draft.Name, OrderItem.NameMaxLength, "El concepto"),
            Reference = OrderText.Require(draft.Reference, OrderItem.ReferenceMaxLength, "La referencia"),
            Quantity = draft.Quantity,
            UnitPriceCents = draft.UnitPriceCents,
            VatRate = draft.VatRate,
            LineTotalCents = draft.LineTotalCents,
            TaxableBaseCents = draft.TaxableBaseCents,
            VatCents = draft.VatCents
        };
    }
}

public sealed record InvoiceLineDraft(
    string Name,
    string Reference,
    int Quantity,
    long UnitPriceCents,
    decimal VatRate,
    long LineTotalCents,
    long TaxableBaseCents,
    long VatCents);
