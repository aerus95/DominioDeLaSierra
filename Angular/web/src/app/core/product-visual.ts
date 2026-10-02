import { CartItem, Product } from './models';

/** Inner edge of the photographed front braid, shared with the landing animation. */
export const BASKET_FRONT_CLIP = 'polygon(0 20%,10% 25%,20% 29%,30% 32%,40% 34%,50% 35%,60% 34%,70% 32%,80% 29%,90% 25%,100% 20%,100% 100%,0 100%)';
export const BASKET_LIP_CENTER = .35;

export function basketProducts(items: CartItem[], maximum = 6): Product[] {
  const visible: Product[] = [];
  for (let round = 0; round < maximum; round++) {
    for (const item of items) {
      if (visible.length === maximum) return visible;
      if (item.quantity > round) visible.push(item.product);
    }
  }
  return visible;
}

/** Positions in the actual opening, not viewport-relative padding. */
export function basketLayout(count: number): { x: number; height: number; tilt: number }[] {
  const size = Math.max(0, Math.min(6, Math.floor(Number.isFinite(count) ? count : 0)));
  const sparse = size <= 2;
  const spacing = sparse ? 20 : size === 3 ? 13 : 9;
  const heights = [56, 59, 54, 60, 57, 55];
  const angles = [-3, 4, -5, 5, -2, 3];
  return Array.from({ length: size }, (_, index) => ({
    x: 50 + (index - (size - 1) / 2) * spacing,
    height: sparse ? (size === 1 ? 64 : 62 + index * 2) : heights[index],
    tilt: size === 1 ? 0 : sparse ? (index === 0 ? -2 : 2) : angles[index]
  }));
}

/** Brand-owned bottle cutouts. Unmapped products retain a neutral bottle. */
export function bottleImage(product: Pick<Product, 'slug' | 'name' | 'reference'>): string {
  const key = `${product.slug} ${product.name} ${product.reference}`.toLowerCase();
  if (key.includes('ancestral')) return '/assets/brand/ancestral-cart.webp';
  if (key.includes('blanco') || key.includes('bla-')) return '/assets/brand/blanco-cart.webp';
  if (key.includes('moment') || key.includes('mom-')) return '/assets/brand/momentum-cart.webp';
  if (key.includes('dominium') || key.includes('dom-')) return '/assets/brand/dominium-cart.webp';
  return '/assets/brand/reservation-bottle-web.png';
}
