namespace DominioDeLaSierra.Domain.Entities;

public sealed class InvoiceSeries
{
    public const int CodeMaxLength = 5;

    public string Code { get; private set; } = null!;
    public int NextNumber { get; private set; }

    private InvoiceSeries()
    {
    }
}
