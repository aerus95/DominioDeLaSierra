export function packQuantityLabel(componentQuantity: number, packQuantity: number): string {
  const perPack = `${componentQuantity} por pack`;
  if (packQuantity <= 1) return perPack;
  return `${perPack} · ${componentQuantity * packQuantity} total`;
}
