export const FREE_SHIPPING_EUROS = 60;

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
  shippingTaxableBaseCents: number | null;
  shippingVatCents: number | null;
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
  shippingTaxableBaseCents: number | null;
  shippingVatCents: number | null;
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
  provider?: string | null;
  redirectMethod?: string | null;
  redirectFields?: Record<string, string> | null;
}

export const CHECKOUT_SUCCESS_PATH = '/checkout/exito';
export const CHECKOUT_CANCEL_PATH = '/checkout/cancelado';
export const CHECKOUT_SUCCESS_MESSAGE = 'El pago está en proceso de verificación. Esta página no confirma el cobro.';
export const CHECKOUT_CANCEL_MESSAGE = 'Has vuelto sin completar el pago. El pedido sigue pendiente y la caja no se ha vaciado.';
export const CHECKOUT_PROCESSING_MESSAGE = 'Estamos confirmando el pago. Recargar esta página no inicia otro cobro.';
export const CHECKOUT_PENDING_MESSAGE = 'El pago sigue pendiente. No hace falta volver a pagarlo desde esta pantalla.';
export const CHECKOUT_CONFIRMED_MESSAGE = 'Pago confirmado.';
export const CHECKOUT_VERIFYING_MESSAGE = 'Estamos verificando el pago.';
export const CHECKOUT_INCOMPLETE_MESSAGE = 'Pago no completado.';
export const CHECKOUT_UNKNOWN_MESSAGE = 'No podemos consultar el estado del pago desde esta página.';
export const CHECKOUT_OUTCOME_ATTEMPTS = 8;
export const CHECKOUT_OUTCOME_DELAYS_MS = [2000, 3000, 5000, 8000, 8000, 8000, 8000] as const;
export const HOLD_RELEASED_MESSAGE = 'No queda margen para un pago de 30 minutos. La reserva se ha liberado.';
export const CHECKOUT_UNAVAILABLE_MESSAGE = 'La compra online estará disponible próximamente';

export type CheckoutGate = 'closed' | 'server';

export function checkoutIsOpen(gate: CheckoutGate, serverAllowed: boolean): boolean {
  return gate === 'server' && serverAllowed === true;
}

export function checkoutUsesPanelCookie(apiBaseUrl: string): boolean {
  const value = apiBaseUrl.trim();
  if (value.length === 0) return true;
  try {
    const url = new URL(value);
    return (url.protocol === 'http:' || url.protocol === 'https:')
      && (url.hostname === 'localhost' || url.hostname === '127.0.0.1');
  } catch {
    return false;
  }
}

export function checkoutAllowedByServer(body: unknown): boolean {
  return !!body
    && typeof body === 'object'
    && 'allowed' in body
    && (body as { allowed?: unknown }).allowed === true;
}

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

export interface PaymentProviderOffer {
  code: string;
  displayName: string;
}

const knownPaymentProviders = new Set(['Stripe', 'Redsys', 'PayPal']);

export function selectablePaymentProviders(
  offers: readonly PaymentProviderOffer[] | null | undefined
): PaymentProviderOffer[] {
  return (offers ?? []).filter((offer) =>
    knownPaymentProviders.has(offer.code) && offer.displayName.trim().length > 0);
}

const paymentProviderDisplayOrder = ['PayPal', 'Redsys', 'Stripe'];
const paymentProviderPreference = ['Stripe', 'PayPal', 'Redsys'];

export function orderedPaymentProviders(
  offers: readonly PaymentProviderOffer[] | null | undefined
): PaymentProviderOffer[] {
  return selectablePaymentProviders(offers).slice().sort((left, right) =>
    paymentProviderRank(left.code) - paymentProviderRank(right.code));
}

export function choosePaymentProvider(
  offers: readonly PaymentProviderOffer[] | null | undefined,
  current: string | null | undefined
): string | null {
  const selectable = selectablePaymentProviders(offers);
  if (current && selectable.some((offer) => offer.code === current)) {
    return current;
  }

  return paymentProviderPreference.find((code) => selectable.some((offer) => offer.code === code)) ?? null;
}

export function paymentMethodNote(code: string | null | undefined): string {
  if (code === 'Redsys') return 'Pasarela bancaria de Redsys';
  if (code === 'PayPal') return 'Cuenta PayPal';
  if (code === 'Stripe') return 'Pago con Stripe';
  return '';
}

function paymentProviderRank(code: string): number {
  const rank = paymentProviderDisplayOrder.indexOf(code);
  return rank < 0 ? paymentProviderDisplayOrder.length : rank;
}

export function placePayload(
  customer: CheckoutCustomerInput,
  lines: readonly CheckoutLineInput[],
  paymentProvider?: string
) {
  const payload: {
    lines: { productId: string; quantity: number }[];
    destination: { postalCode: string; countryCode: string };
    customerName: string;
    email: string;
    phone: string;
    addressLine: string;
    city: string;
    province: string;
    deliveryNotes: string;
    paymentProvider?: string;
  } = {
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
  if (paymentProvider) {
    payload.paymentProvider = paymentProvider;
  }
  return payload;
}

export function paymentSessionPayload(orderId: string) {
  return { orderId };
}

export function checkoutOutcomePayload(orderId: string) {
  return { orderId };
}

export type CheckoutOutcomeView = 'loading' | 'confirmed' | 'verifying' | 'incomplete' | 'unknown';

export interface CheckoutOutcome {
  number: string;
  status: string;
  totalCents: number;
  currency: string;
}

export interface CurrentCheckout {
  orderId: string;
  token: string;
  provider: string | null;
}

const currentCheckoutKey = 'ds-checkout-current';

export function rememberCurrentCheckout(
  orderId: string,
  token: string | null | undefined,
  storage: Pick<Storage, 'getItem' | 'setItem'> | null,
  provider?: string | null
): void {
  if (!orderId || !token) return;
  const known = provider === 'Stripe' || provider === 'Redsys' || provider === 'PayPal' ? provider : null;
  storage?.setItem(currentCheckoutKey, JSON.stringify({ orderId, token, provider: known }));
}

export function readCurrentCheckout(storage: Pick<Storage, 'getItem'> | null): CurrentCheckout | null {
  const raw = storage?.getItem(currentCheckoutKey);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as { orderId?: unknown; token?: unknown; provider?: unknown };
    if (typeof parsed.orderId !== 'string' || typeof parsed.token !== 'string') return null;
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(parsed.orderId)) return null;
    if (parsed.token.length < 20 || parsed.token.length > 128) return null;
    const provider = parsed.provider === 'Stripe' || parsed.provider === 'Redsys' || parsed.provider === 'PayPal'
      ? parsed.provider
      : null;
    return { orderId: parsed.orderId, token: parsed.token, provider };
  } catch {
    return null;
  }
}

export function checkoutOutcomeView(status: string | null, failed: boolean): CheckoutOutcomeView {
  if (failed || status === null) return 'unknown';
  if (status === 'Paid') return 'confirmed';
  if (status === 'PendingPayment') return 'verifying';
  if (status === 'Expired' || status === 'Cancelled') return 'incomplete';
  return 'unknown';
}

export function checkoutOutcomeCopy(view: CheckoutOutcomeView): string {
  if (view === 'confirmed') return CHECKOUT_CONFIRMED_MESSAGE;
  if (view === 'verifying') return CHECKOUT_VERIFYING_MESSAGE;
  if (view === 'incomplete') return CHECKOUT_INCOMPLETE_MESSAGE;
  if (view === 'loading') return 'Estamos consultando el pago.';
  return CHECKOUT_UNKNOWN_MESSAGE;
}

export function nextCheckoutPoll(view: CheckoutOutcomeView, completed: number): number | null {
  if (view !== 'verifying' || completed >= CHECKOUT_OUTCOME_ATTEMPTS) return null;
  return CHECKOUT_OUTCOME_DELAYS_MS[Math.min(completed - 1, CHECKOUT_OUTCOME_DELAYS_MS.length - 1)] ?? null;
}

export function checkoutAmount(cents: number, currency: string): string | null {
  if (!Number.isInteger(cents) || cents < 0 || currency !== 'EUR') return null;
  return new Intl.NumberFormat('es-ES', { style: 'currency', currency: 'EUR' }).format(cents / 100);
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

export function paypalSandboxCheckoutUrl(value: string): string | null {
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.hostname !== 'www.sandbox.paypal.com' || url.username || url.password) return null;
    return url.toString();
  } catch {
    return null;
  }
}

export function paypalReturnShouldCapture(
  routePath: string | null | undefined,
  status: string | null | undefined,
  provider: string | null | undefined
): boolean {
  if (routePath !== 'checkout/exito' || status !== 'PendingPayment') return false;
  return provider === 'PayPal' || provider == null;
}

export function paypalCaptureClaim(
  orderId: string,
  nowMs: number,
  storage: Pick<Storage, 'getItem' | 'setItem'> | null,
  leaseMs = 25_000
): 'owner' | 'wait' {
  if (!storage) return 'owner';
  const key = `ds-paypal-capture:${orderId}`;
  const until = Number(storage.getItem(key));
  if (Number.isFinite(until) && until > nowMs) return 'wait';
  storage.setItem(key, String(nowMs + leaseMs));
  return 'owner';
}

export const REDSYS_TEST_ENDPOINT = 'https://sis-t.redsys.es:25443/sis/realizarPago';

export function redsysTestEndpoint(value: string): string | null {
  try {
    const url = new URL(value);
    if (url.protocol !== 'https:' || url.hostname !== 'sis-t.redsys.es' || url.port !== '25443' || url.pathname !== '/sis/realizarPago') {
      return null;
    }
    return `${url.origin}${url.pathname}`;
  } catch {
    return null;
  }
}

export type PaymentRedirect =
  | { method: 'get'; url: string }
  | { method: 'post'; url: string; fields: Record<string, string> };

export function paymentRedirect(payment: Pick<CheckoutPayment, 'checkoutUrl' | 'redirectMethod' | 'redirectFields' | 'provider'>): PaymentRedirect | null {
  if (payment.redirectMethod === 'POST') {
    const url = redsysTestEndpoint(payment.checkoutUrl);
    const fields = payment.redirectFields ?? {};
    const version = fields['Ds_SignatureVersion'];
    const parameters = fields['Ds_MerchantParameters'];
    const signature = fields['Ds_Signature'];
    const names = Object.keys(fields);
    if (!url || version !== 'HMAC_SHA512_V2' || !parameters || !signature) return null;
    if (names.some((name) => name !== 'Ds_SignatureVersion' && name !== 'Ds_MerchantParameters' && name !== 'Ds_Signature')) {
      return null;
    }
    return {
      method: 'post',
      url,
      fields: {
        Ds_SignatureVersion: version,
        Ds_MerchantParameters: parameters,
        Ds_Signature: signature
      }
    };
  }

  if (payment.provider === 'PayPal') {
    const paypal = paypalSandboxCheckoutUrl(payment.checkoutUrl);
    return paypal ? { method: 'get', url: paypal } : null;
  }

  const stripe = stripeCheckoutUrl(payment.checkoutUrl);
  return stripe ? { method: 'get', url: stripe } : null;
}

export function providerSent(selected: string | null | undefined): string | undefined {
  return selected === 'Stripe' || selected === 'Redsys' || selected === 'PayPal' ? selected : undefined;
}

export const paymentSimulationNotice = 'Pagos de prueba. No se realiza ningún cargo real.';

export function paymentButtonLabel(selected: string | null | undefined): string {
  if (selected === 'PayPal') return 'Pagar con PayPal';
  if (selected === 'Redsys') return 'Pagar con tarjeta';
  if (selected === 'Stripe') return 'Pagar con Stripe';
  return 'El pago no está disponible';
}

export function submitPaymentForm(action: string, fields: Record<string, string>, document: Document): void {
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = action;
  form.acceptCharset = 'UTF-8';
  for (const [name, value] of Object.entries(fields)) {
    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = name;
    input.value = value;
    form.appendChild(input);
  }
  document.body.appendChild(form);
  form.submit();
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
