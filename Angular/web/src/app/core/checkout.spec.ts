import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  CHECKOUT_SUCCESS_MESSAGE,
  CHECKOUT_UNAVAILABLE_MESSAGE,
  checkoutAllowedByServer,
  checkoutUsesPanelCookie,
  checkoutIsOpen,
  FREE_SHIPPING_EUROS,
  HOLD_RELEASED_MESSAGE,
  checkoutErrorMessage,
  checkoutSignature,
  eurosFromCents,
  nextCheckoutPhase,
  paymentSessionPayload,
  placePayload,
  quotePayload,
  selectablePaymentProviders,
  CHECKOUT_CANCEL_PATH,
  CHECKOUT_CONFIRMED_MESSAGE,
  CHECKOUT_INCOMPLETE_MESSAGE,
  CHECKOUT_SUCCESS_PATH,
  CHECKOUT_UNKNOWN_MESSAGE,
  CHECKOUT_VERIFYING_MESSAGE,
  checkoutAmount,
  checkoutOutcomeCopy,
  checkoutOutcomePayload,
  checkoutOutcomeView,
  nextCheckoutPoll,
  paymentRedirect,
  paypalCaptureClaim,
  paypalReturnShouldCapture,
  paypalSandboxCheckoutUrl,
  paymentButtonLabel,
  providerSent,
  readCheckoutAccess,
  readCurrentCheckout,
  forgetIdempotencyKey,
  rememberCheckoutAccess,
  rememberCurrentCheckout,
  resolveIdempotencyKey,
  stripeCheckoutUrl
} from './checkout.ts';

const customer = {
  name: 'Ana Rivas',
  email: 'ana@example.com',
  phone: '+34600111222',
  address: 'Calle Mayor 1',
  postalCode: '37001',
  city: 'Salamanca',
  province: 'Salamanca',
  notes: ''
};

describe('disponibilidad del checkout', () => {
  it('solo abre la compra cuando el servidor autoriza la sesión', () => {
    assert.equal(checkoutIsOpen('closed', true), false);
    assert.equal(checkoutIsOpen('server', false), false);
    assert.equal(checkoutIsOpen('server', true), true);
    assert.equal(checkoutAllowedByServer({ allowed: true }), true);
    assert.equal(checkoutAllowedByServer({ allowed: 'true' }), false);
    assert.equal(checkoutAllowedByServer({ enabled: true }), false);
    assert.equal(checkoutAllowedByServer(null), false);
    assert.equal(CHECKOUT_UNAVAILABLE_MESSAGE, 'La compra online estará disponible próximamente');
    assert.equal(checkoutUsesPanelCookie(''), true);
    assert.equal(checkoutUsesPanelCookie('http://localhost:5141'), true);
    assert.equal(checkoutUsesPanelCookie('http://127.0.0.1:5141'), true);
    assert.equal(checkoutUsesPanelCookie('https://api.example.com'), false);
  });
});

describe('presupuesto del checkout', () => {
  it('pide al servidor solo identificadores, cantidades y destino', () => {
    const payload = quotePayload(
      [{ productId: 'wine', quantity: 2, price: 18.9 } as { productId: string; quantity: number }],
      '37001'
    );
    assert.deepEqual(payload, {
      lines: [{ productId: 'wine', quantity: 2 }],
      destination: { postalCode: '37001', countryCode: 'ES' }
    });
    assert.equal('price' in payload.lines[0], false);
  });

  it('mantiene el umbral de envío gratuito en 60 euros', () => {
    assert.equal(FREE_SHIPPING_EUROS, 60);
    assert.equal(eurosFromCents(600), 6);
    assert.equal(eurosFromCents(6_000), 60);
  });

  it('reutiliza la clave de idempotencia para el mismo pedido', () => {
    const memory = new Map<string, string>();
    const storage = {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => { memory.set(key, value); }
    };
    const signature = checkoutSignature(customer, [{ productId: 'wine', quantity: 1 }]);
    const first = resolveIdempotencyKey(signature, storage);
    const second = resolveIdempotencyKey(signature, storage);
    const changed = resolveIdempotencyKey(checkoutSignature(customer, [{ productId: 'wine', quantity: 2 }]), storage);
    assert.equal(first, second);
    assert.notEqual(first, changed);
  });

  it('no incluye el precio del carrito al crear el pedido', () => {
    const payload = placePayload(customer, [{ productId: 'wine', quantity: 1 }]);
    assert.deepEqual(payload.lines, [{ productId: 'wine', quantity: 1 }]);
    assert.equal(JSON.stringify(payload).includes('price'), false);
  });

  it('muestra el error del servidor y el estado de carga', () => {
    assert.equal(checkoutErrorMessage({ error: ' No hay stock suficiente. ' }, 'fallback'), 'No hay stock suficiente.');
    assert.equal(checkoutErrorMessage(null, 'No hemos podido calcular el presupuesto.'), 'No hemos podido calcular el presupuesto.');
    assert.equal(nextCheckoutPhase('idle', 'quote'), 'quoting');
    assert.equal(nextCheckoutPhase('quoting', 'quote-failed'), 'error');
    assert.equal(nextCheckoutPhase('quoted', 'place'), 'placing');
    assert.equal(nextCheckoutPhase('placing', 'placed'), 'pending');
    assert.equal(nextCheckoutPhase('placing', 'place-failed'), 'error');
    assert.equal(nextCheckoutPhase('quoting', 'place'), 'quoting');
    assert.equal(nextCheckoutPhase('pending', 'pay'), 'paying');
    assert.equal(nextCheckoutPhase('quoted', 'pay'), 'paying');
    assert.equal(nextCheckoutPhase('paying', 'pay-failed'), 'quoted');
    assert.equal(JSON.stringify(placePayload(customer, [{ productId: 'wine', quantity: 1 }])).includes('Paid'), false);
    assert.match(HOLD_RELEASED_MESSAGE, /liberado/);
  });

  it('abre Stripe solo con la dirección devuelta y sin importes del navegador', () => {
    const payload = paymentSessionPayload('pedido-1');
    assert.deepEqual(payload, { orderId: 'pedido-1' });
    assert.equal(JSON.stringify(payload).includes('amount'), false);
    assert.equal(JSON.stringify(payload).includes('price'), false);
    assert.equal(
      stripeCheckoutUrl('https://checkout.stripe.com/c/pay/cs_test_abc'),
      'https://checkout.stripe.com/c/pay/cs_test_abc'
    );
    assert.equal(stripeCheckoutUrl('http://checkout.stripe.com/c/pay/cs_test_abc'), null);
    assert.equal(stripeCheckoutUrl('https://evil.example/checkout'), null);
    assert.match(CHECKOUT_SUCCESS_MESSAGE, /verificación/);
    assert.doesNotMatch(CHECKOUT_SUCCESS_MESSAGE, /pago confirmado/i);
  });

  it('conserva el token de acceso si el reintento no lo devuelve', () => {
    const memory = new Map<string, string>();
    const storage = {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => { memory.set(key, value); }
    };
    rememberCheckoutAccess('pedido-1', 'token-secreto', storage);
    rememberCheckoutAccess('pedido-1', null, storage);
    assert.equal(readCheckoutAccess('pedido-1', storage), 'token-secreto');
    const signature = checkoutSignature(customer, [{ productId: 'wine', quantity: 1 }]);
    const removable = {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => { memory.set(key, value); },
      removeItem: (key: string) => { memory.delete(key); }
    };
    const key = resolveIdempotencyKey(signature, removable);
    forgetIdempotencyKey(signature, removable);
    assert.notEqual(key, resolveIdempotencyKey(signature, removable));
  });

  it('consulta el estado con el pedido y el token, sin datos personales ni claves', () => {
    const orderId = 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff';
    const payload = checkoutOutcomePayload(orderId);
    assert.deepEqual(payload, { orderId });
    assert.equal(JSON.stringify(payload).includes('token'), false);
    assert.equal(JSON.stringify(payload).includes('email'), false);
    assert.equal(CHECKOUT_SUCCESS_PATH.includes('?'), false);
    assert.equal(CHECKOUT_CANCEL_PATH.includes('?'), false);
    assert.equal(checkoutOutcomeView('Paid', false), 'confirmed');
    assert.equal(checkoutOutcomeView('PendingPayment', false), 'verifying');
    assert.equal(checkoutOutcomeView('Expired', false), 'incomplete');
    assert.equal(checkoutOutcomeView('Cancelled', false), 'incomplete');
    assert.equal(checkoutOutcomeView('Paid', true), 'unknown');
    assert.equal(checkoutOutcomeView('Refunded', false), 'unknown');
    assert.equal(checkoutOutcomeCopy('confirmed'), CHECKOUT_CONFIRMED_MESSAGE);
    assert.equal(checkoutOutcomeCopy('verifying'), CHECKOUT_VERIFYING_MESSAGE);
    assert.equal(checkoutOutcomeCopy('incomplete'), CHECKOUT_INCOMPLETE_MESSAGE);
    assert.match(checkoutOutcomeCopy('unknown'), /consultar/);
    assert.equal(CHECKOUT_UNKNOWN_MESSAGE.includes('fall'), false);
    assert.equal(nextCheckoutPoll('verifying', 1), 2000);
    assert.equal(nextCheckoutPoll('verifying', 8), null);
    assert.equal(nextCheckoutPoll('confirmed', 1), null);
    const amount = checkoutAmount(1700, 'EUR')?.replace(/\s/g, ' ');
    assert.match(amount ?? '', /17,00/);
    assert.match(amount ?? '', /€/);
    assert.equal(checkoutAmount(1700, 'USD'), null);
  });

  it('recuerda el checkout actual y no lo sustituye por un reintento sin token', () => {
    const memory = new Map<string, string>();
    const storage = {
      getItem: (key: string) => memory.get(key) ?? null,
      setItem: (key: string, value: string) => { memory.set(key, value); }
    };
    const orderId = 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff';
    rememberCurrentCheckout(orderId, 'token-opaco-de-acceso-1', storage);
    rememberCurrentCheckout(orderId, null, storage);
    assert.deepEqual(readCurrentCheckout(storage), { orderId, token: 'token-opaco-de-acceso-1', provider: null });
    rememberCurrentCheckout(orderId, 'token-opaco-de-acceso-1', storage, 'PayPal');
    assert.equal(readCurrentCheckout(storage)?.provider, 'PayPal');
    assert.equal(readCurrentCheckout(storage)?.token.includes('sk_'), false);
  });

  it('solo deja elegir proveedores anunciados y no los envía si no hay selección', () => {
    assert.deepEqual(
      selectablePaymentProviders([
        { code: 'Stripe', displayName: 'Stripe' },
        { code: 'Redsys', displayName: 'Redsys' },
        { code: 'Bizum', displayName: 'Bizum' },
        { code: 'PayPal', displayName: '   ' }
      ]).map((offer) => offer.code),
      ['Stripe', 'Redsys']
    );
    assert.deepEqual(selectablePaymentProviders(null), []);
    const payload = placePayload(customer, [{ productId: 'wine', quantity: 1 }]);
    assert.equal('paymentProvider' in payload, false);
    assert.equal(placePayload(customer, [{ productId: 'wine', quantity: 1 }], 'Stripe').paymentProvider, 'Stripe');
    assert.equal(providerSent('Redsys'), 'Redsys');
    assert.equal(providerSent('PayPal'), 'PayPal');
    assert.equal(providerSent('Stripe'), 'Stripe');
    assert.equal(providerSent(null), undefined);
    assert.equal(providerSent('Bizum'), undefined);
    assert.equal(paymentButtonLabel('PayPal'), 'Pagar con PayPal');
    assert.equal(paymentButtonLabel('Redsys'), 'Pagar con tarjeta');
    assert.equal(paymentButtonLabel('Stripe'), 'Pagar con Stripe');
    assert.equal(paymentButtonLabel(null), 'El pago no está disponible');
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'Paid', 'PayPal'), false);
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'PendingPayment', 'PayPal'), true);
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'PendingPayment', null), true);
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'PendingPayment', 'Stripe'), false);
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'PendingPayment', 'Redsys'), false);
    assert.equal(paypalReturnShouldCapture('checkout/exito', 'Cancelled', 'PayPal'), false);
    assert.equal(paypalReturnShouldCapture('checkout/cancelado', 'PendingPayment', 'PayPal'), false);
    const claims = new Map<string, string>();
    const claimStorage = {
      getItem: (key: string) => claims.get(key) ?? null,
      setItem: (key: string, value: string) => { claims.set(key, value); }
    };
    const claimOrder = 'bbbbbbbb-cccc-dddd-eeee-ffffffffffff';
    assert.equal(paypalCaptureClaim(claimOrder, 1_000, claimStorage), 'owner');
    assert.equal(paypalCaptureClaim(claimOrder, 1_000, claimStorage), 'wait');
    assert.equal(paypalCaptureClaim(claimOrder, 30_000, claimStorage), 'owner');
    assert.equal(paypalSandboxCheckoutUrl('https://www.paypal.com/checkoutnow?token=1'), null);
    const fields = {
      Ds_SignatureVersion: 'HMAC_SHA512_V2',
      Ds_MerchantParameters: 'parametros',
      Ds_Signature: 'firma'
    };
    assert.deepEqual(paymentRedirect({
      checkoutUrl: 'https://sis-t.redsys.es:25443/sis/realizarPago',
      redirectMethod: 'POST',
      redirectFields: fields
    }), { method: 'post', url: 'https://sis-t.redsys.es:25443/sis/realizarPago', fields });
    assert.equal(paymentRedirect({
      checkoutUrl: 'https://sis.redsys.es/sis/realizarPago',
      redirectMethod: 'POST',
      redirectFields: fields
    }), null);
    assert.deepEqual(paymentRedirect({
      checkoutUrl: 'https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T',
      redirectMethod: 'GET',
      provider: 'PayPal'
    }), { method: 'get', url: 'https://www.sandbox.paypal.com/checkoutnow?token=5O190127TN364715T' });
    assert.equal(paymentRedirect({
      checkoutUrl: 'https://www.paypal.com/checkoutnow?token=LIVE',
      redirectMethod: 'GET',
      provider: 'PayPal'
    }), null);
  });
});
