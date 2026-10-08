type CatalogKind = 'Standard' | 'Wine' | 'Pack';

const alcoholFormat = new Intl.NumberFormat('es-ES', {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2
});

export function wineGrape(kind: CatalogKind, grape: string | null | undefined): string {
  if (kind !== 'Wine') return '';
  return grape?.trim() ?? '';
}

export function wineAlcohol(kind: CatalogKind, alcoholPercent: number | null | undefined): string {
  if (kind !== 'Wine' || alcoholPercent == null || !Number.isFinite(alcoholPercent)) return '';
  return `${alcoholFormat.format(alcoholPercent)}% vol.`;
}

export function showsWineSpec(value: string): boolean {
  return value.length > 0;
}

export function categoryGrapeLabel(categoryName: string, grape: string): string {
  return showsWineSpec(grape) ? `${categoryName} · ${grape}` : categoryName;
}
