import { Product } from './models';
import { BASKET_LIP_CENTER } from './product-visual';

let sequence = 0;
let cancelFlight: (() => void) | undefined;

export function bottleFlightFrames(dx: number, dy: number, scale: number, tilt: number, visible = 1, landingHeight = 56): Keyframe[] {
  const hidden = Math.max(0, Math.min(100, (1 - visible) * 100));
  const approaching = Math.max(0, hidden - 10 / landingHeight * 100);
  return [
    { transform:'translate(0,0) scale(1) rotate(-4deg)', clipPath:'inset(0 0 0% 0)', opacity:1, offset:0 },
    { transform:`translate(${dx * .2}px,${dy * .12 - 38}px) scale(.94) rotate(-6deg)`, clipPath:'inset(0 0 0% 0)', opacity:1, offset:.24 },
    { transform:`translate(${dx * .84}px,${dy * .84 - 28}px) scale(${scale * 1.15}) rotate(${tilt - 2}deg)`, clipPath:'inset(0 0 0% 0)', opacity:1, offset:.72 },
    { transform:`translate(${dx}px,${dy - 10}px) scale(${scale}) rotate(${tilt}deg)`, clipPath:`inset(0 0 ${approaching}% 0)`, opacity:1, offset:.90 },
    { transform:`translate(${dx}px,${dy}px) scale(${scale}) rotate(${tilt}deg)`, clipPath:`inset(0 0 ${hidden}% 0)`, opacity:0, offset:1 }
  ];
}

/** Quantities update immediately; only this presentation is asynchronous. */
export async function animateBottleToCart(event: MouseEvent, product: Product): Promise<void> {
  const ticket = ++sequence;
  cancelFlight?.();
  cancelFlight = undefined;
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches || typeof Element.prototype.animate !== 'function') return;
  const button = event.currentTarget as HTMLElement;
  const scope = button?.closest('.product-card, .quick-view-modal, .product-detail');
  const source = scope?.querySelector('img');
  const basket = document.querySelector<HTMLElement>('.harvest-basket');
  if (!source || !basket) return;
  const flyer = new Image();
  flyer.src = product.image;
  flyer.alt = '';
  flyer.setAttribute('aria-hidden', 'true');
  await flyer.decode().catch(() => undefined);
  if (!flyer.naturalWidth) return;
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
  if (ticket !== sequence || !basket.isConnected || !button.isConnected) return;
  const slot = Array.from(basket.querySelectorAll<HTMLElement>('.harvest-slot')).reverse().find(element => element.dataset['productId'] === product.id);
  const from = source.getBoundingClientRect();
  const to = slot?.getBoundingClientRect() ?? basket.getBoundingClientRect();
  const height = window.innerWidth <= 600 ? 144 : 168;
  const width = height / 3;
  const startX = from.left + from.width / 2;
  const startY = Math.max(90, Math.min(window.innerHeight - 70, from.top + from.height / 2));
  const dx = to.left + to.width / 2 - startX, dy = to.top + to.height / 2 - startY;
  const scale = Math.min(.55, Math.max(.25, to.height / height));
  const tilt = Number.parseFloat(slot?.style.getPropertyValue('--tilt') || '0') || 0;
  const front = basket.querySelector<HTMLElement>('.harvest-front')?.getBoundingClientRect();
  const visible = front && to.height > 0 ? (front.top + front.height * BASKET_LIP_CENTER - to.top) / to.height : 1;
  flyer.className = 'cart-flight-bottle';
  Object.assign(flyer.style, { left:`${startX - width / 2}px`, top:`${startY - height / 2}px`, width:`${width}px`, height:`${height}px` });
  button.classList.add('is-adding-to-cart');
  document.body.appendChild(flyer);
  let animation: Animation;
  try { animation = flyer.animate(bottleFlightFrames(dx, dy, scale, tilt, visible, to.height), { duration:Math.min(950, 700 + Math.hypot(dx, dy) * .12), easing:'cubic-bezier(.25,.1,.25,1)', fill:'forwards' }); }
  catch { flyer.remove(); button.classList.remove('is-adding-to-cart'); return; }
  let disposed = false;
  const cleanup = () => { if (disposed) return; disposed = true; animation.cancel(); flyer.remove(); button.classList.remove('is-adding-to-cart'); };
  cancelFlight = cleanup;
  try {
    await animation.finished;
    if (ticket !== sequence) return;
    if (slot?.isConnected) {
      const resting = getComputedStyle(slot).transform;
      slot.animate([{ transform:`translateY(-3px) ${resting}`, opacity:.7 }, { transform:resting, opacity:1 }], { duration:260, easing:'cubic-bezier(.22,1,.36,1)' });
    }
    basket.animate([{ transform:'translateY(0)' }, { transform:'translateY(1.5px)' }, { transform:'translateY(0)' }], { duration:280, easing:'ease-out' });
    basket.querySelector('.harvest-count')?.animate([{ transform:'scale(1)' }, { transform:'scale(1.16)' }, { transform:'scale(1)' }], { duration:300, easing:'ease-out' });
  } catch { /* A later addition supersedes this flight without affecting the cart. */ }
  finally { cleanup(); if (ticket === sequence) cancelFlight = undefined; }
}
