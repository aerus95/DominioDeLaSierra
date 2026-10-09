import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import {
  CHECKOUT_SUCCESS_MESSAGE,
  FREE_SHIPPING_EUROS,
  HOLD_RELEASED_MESSAGE,
  checkoutErrorMessage,
  checkoutSignature,
  eurosFromCents,
  nextCheckoutPhase,
  paymentSessionPayload,
  placePayload,
  quotePayload,
  readCheckoutAccess,
  forgetIdempotencyKey,
  rememberCheckoutAccess,
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

  it('mantiene el umbral de envío gratuito en 100 euros', () => {
    assert.equal(FREE_SHIPPING_EUROS, 100);
    assert.equal(eurosFromCents(800), 8);
    assert.equal(eurosFromCents(10_000), 100);
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
});
