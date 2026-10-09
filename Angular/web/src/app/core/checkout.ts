export const FREE_SHIPPING_EUROS = 100;

export interface CheckoutLineInput {
  productId: string;
  quantity: number;
}

export interface CheckoutCustomerInput {
  name: string;
  email: string;
  phone: string;
  address: string;
  postalCode: string;
  city: string;
  province: string;
  notes: string;
}

export interface CheckoutQuoteLine {
  productId: string;
  name: string;
  quantity: number;
  unitPriceCents: number;
  vatRate: number;
  lineTotalCents: number;
  taxableBaseCents: number;
  vatCents: number;
}

export interface CheckoutQuote {
  lines: CheckoutQuoteLine[];
  productSubtotalCents: number;
  productTaxableBaseCents: number;
  productVatCents: number;
  shippingCents: number;
  shippingVatRate: number | null;
  totalCents: number;
  currency: string;
  shippingVatPending: boolean;
}

export interface GuestOrder {
  orderId: string;
  number: string;
  productSubtotalCents: number;
  productTaxableBaseCents: number;
  productVatCents: number;
  shippingCents: number;
  shippingVatRate: number | null;
  totalCents: number;
  currency: string;
  reservationExpiresAt: string;
  status: string;
  shippingVatPending: boolean;
  checkoutAccessToken?: string | null;
}

export interface CheckoutSession {
  checkoutUrl: string;
  expiresAt: string;
}

export interface CheckoutPayment {
  orderId: string;
  number: string;
  totalCents: number;
  reservationExpiresAt: string;
  status: string;
  checkoutUrl: string;
  expiresAt: string;
  checkoutAccessToken?: string | null;
}

export const CHECKOUT_SUCCESS_MESSAGE = 'El pago está en proceso de verificación. Esta página no confirma el cobro.';
export const CHECKOUT_CANCEL_MESSAGE = 'Has vuelto sin completar el pago. El pedido sigue pendiente y la caja no se ha vaciado.';
export const HOLD_RELEASED_MESSAGE = 'No queda margen para un pago de 30 minutos. La reserva se ha liberado.';

export function eurosFromCents(cents: number): number {
  return cents / 100;
}

export function checkoutSignature(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[]): string {
  const products = [...lines]
    .map((line) => `${line.productId}:${line.quantity}`)
    .sort()
    .join(',');
  return [
    customer.name.trim(),
    customer.email.trim(),
    customer.phone.trim(),
    customer.address.trim(),
    customer.postalCode.trim(),
    customer.city.trim(),
    customer.province.trim(),
    customer.notes.trim(),
    products
  ].join('|');
}

export function quotePayload(lines: readonly CheckoutLineInput[], postalCode: string) {
  return {
    lines: lines.map((line) => ({ productId: line.productId, quantity: line.quantity })),
    destination: { postalCode, countryCode: 'ES' }
  };
}

export function placePayload(customer: CheckoutCustomerInput, lines: readonly CheckoutLineInput[]) {
  return {
    lines: lines.map((line) => ({ productId: line.productId, quantity: line.quantity })),
    destination: { postalCode: customer.postalCode.trim(), countryCode: 'ES' },
    customerName: customer.name,
    email: customer.email,
    phone: customer.phone,
    addressLine: customer.address,
    city: customer.city,
    province: customer.province,
    deliveryNotes: customer.notes
  };
}

export function paymentSessionPayload(orderId: string) {
  return { orderId };
}

export function rememberCheckoutAccess(
  orderId: string,
  token: string | null | undefined,
  storage: Pick<Storage, 'getItem' | 'setItem'> | null
): void {
  if (!token) return;
  storage?.setItem(`ds-checkout-access:${orderId}`, token);
}

export function readCheckoutAccess(orderId: string, storage: Pick<Storage, 'getItem'> | null): string | null {
  return storage?.getItem(`ds-checkout-access:${orderId}`) ?? null;
}

export function stripeCheckoutUrl(value: string): string | null {
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.hostname !== 'checkout.stripe.com') return null;
    return url.toString();
  } catch {
    return null;
  }
}

export function forgetIdempotencyKey(signature: string, storage: Pick<Storage, 'removeItem'> | null): void {
  storage?.removeItem(`ds-checkout-key:${signature}`);
}

export function resolveIdempotencyKey(signature: string, storage: Pick<Storage, 'getItem' | 'setItem'> | null): string {
  const name = `ds-checkout-key:${signature}`;
  const existing = storage?.getItem(name);
  if (existing) return existing;
  const key = crypto.randomUUID();
  storage?.setItem(name, key);
  return key;
}

export function checkoutErrorMessage(body: unknown, fallback: string): string {
  if (body && typeof body === 'object' && 'error' in body && typeof body.error === 'string' && body.error.trim()) {
    return body.error.trim();
  }
  return fallback;
}

export type CheckoutPhase = 'idle' | 'quoting' | 'quoted' | 'placing' | 'pending' | 'paying' | 'error';

export function nextCheckoutPhase(
  phase: CheckoutPhase,
  event: 'quote' | 'quoted' | 'quote-failed' | 'place' | 'placed' | 'place-failed' | 'pay' | 'pay-failed'
): CheckoutPhase {
  if (event === 'quote') return 'quoting';
  if (event === 'quoted') return 'quoted';
  if (event === 'quote-failed') return 'error';
  if (event === 'place' && (phase === 'quoted' || phase === 'error')) return 'placing';
  if (event === 'placed') return 'pending';
  if (event === 'place-failed') return 'error';
  if (event === 'pay' && (phase === 'pending' || phase === 'quoted' || phase === 'error')) return 'paying';
  if (event === 'pay-failed') return 'quoted';
  return phase;
}
