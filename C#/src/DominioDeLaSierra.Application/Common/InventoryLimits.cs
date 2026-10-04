using DominioDeLaSierra.Domain.Entities;

namespace DominioDeLaSierra.Application.Common;

public static class InventoryLimits
{
    public const int MaxStockQuantity = 1_000_000;
    public const string InitialStockNote = "Stock inicial";
    public const string AdjustmentNote = "Ajuste manual";
    public const string PrincipalWarehouseCode = "principal";
    public const string PrincipalWarehouseName = "Bodega";
    public const int MovementNoteMaxLength = StockMovement.NoteMaxLength;
}
